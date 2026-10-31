using System.Globalization;
using Arma.Infrastructure.Messaging;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Invoicing.Contracts;
using Arma.Modules.Invoicing.Domain;
using Arma.Modules.Invoicing.Infrastructure;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Invoicing;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Invoicing.Application;

public sealed record InvoiceFilter(InvoiceStatus? Status = null, Guid? ContactId = null, string? Search = null, bool? PendingTransmission = null);

public sealed record InvoiceListItem(
    Guid Id, string? Number, InvoiceStatus Status, EInvoiceProfiles Profile, DateOnly IssuedOn, Guid ContactId, string BuyerName, string SourceKind,
    string? SourceLabel, decimal Total, string? Provider, bool Transmitted, string? LastError);

/// <summary>Fatura ayarları ekranındaki entegratör satırı.</summary>
public sealed record EInvoiceProviderStatus(EInvoiceProviderInfo Info, bool IsSelected);

public sealed record InvoiceDraftInput(InvoiceBuyer Buyer, EInvoiceProfiles Profile, string? Note);

public sealed class InvoiceService(InvoicingDbContext db, IEInvoiceGateway gateway, IContactBillingDirectory contacts, IDbContextOutbox outbox, TimeProvider clock)
{
    public const string SourceModule = "invoicing";

    public const string ManualSource = "manual";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IReadOnlyList<InvoiceListItem>> ListAsync(InvoiceFilter filter, CancellationToken cancellationToken = default)
    {
        var query = db.Invoices.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(i => i.Status == status);
        }

        if (filter.ContactId is { } contactId)
        {
            query = query.Where(i => i.ContactId == contactId);
        }

        if (filter.PendingTransmission == true)
        {
            query = query.Where(i => i.Status == InvoiceStatus.Issued && i.TransmittedAt == null);
        }

        if (filter.Search?.Trim() is { Length: > 0 } term)
        {
            var pattern = $"%{term}%";
            query = query.Where(i => EF.Functions.ILike(i.BuyerName, pattern) || (i.Number != null && EF.Functions.ILike(i.Number, pattern))
                || (i.SourceLabel != null && EF.Functions.ILike(i.SourceLabel, pattern)));
        }

        var rows = await query.OrderByDescending(i => i.IssuedOn).ThenByDescending(i => i.Number).ThenByDescending(i => i.CreatedAt).Take(500).ToListAsync(cancellationToken);
        return [.. rows.Select(i => new InvoiceListItem(i.Id, i.Number, i.Status, i.Profile, i.IssuedOn, i.ContactId, i.BuyerName, i.SourceKind, i.SourceLabel, i.Total,
            i.Provider, i.TransmittedAt is not null, i.LastError))];
    }

    public Task<Invoice?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, cancellationToken);

    /// <summary>
    /// Başka modülün isteğiyle fatura açar. Kaynak türü ayarlarda kapalıysa veya bu kaynak için fatura zaten varsa
    /// hiçbir şey yapmaz (null). Otomatik gönderim açıksa fatura hemen gönderilir; gönderilemezse taslak kalır.
    /// </summary>
    public async Task<Guid?> RequestAsync(InvoiceRequested request, CancellationToken cancellationToken = default)
    {
        var settings = await SettingsAsync(cancellationToken);
        var enabled = request.SourceKind switch
        {
            InvoiceSources.Quote => settings.FromQuotes,
            InvoiceSources.Dues => settings.FromDues,
            _ => true,
        };
        if (!enabled || await db.Invoices.IgnoreQueryFilters().AnyAsync(i => i.TenantId == db.TenantId && i.SourceKind == request.SourceKind && i.SourceId == request.SourceId, cancellationToken))
        {
            return null;
        }

        if (await contacts.FindBillingAsync(request.ContactId, cancellationToken) is not { } contact)
        {
            return null;
        }

        var lines = request.Lines.Select(l =>
        {
            var vat = l.VatRate ?? settings.DuesVatRate;
            var price = l.VatIncluded ? EInvoiceMath.Round(l.UnitPrice * 100m / (100m + vat)) : l.UnitPrice;
            return new InvoiceLineInput(l.Description, l.Quantity, l.Unit, price, l.DiscountRate, vat);
        }).ToList();
        var created = Invoice.Create(contact.Id, request.SourceKind, request.SourceId, request.SourceLabel, request.IssuedOn, BuyerOf(contact), lines, request.Note);
        if (!created.IsSuccess)
        {
            // Kişide geçersiz kimlik numarası varsa fatura kimliksiz açılır; görevli taslakta düzeltir.
            created = Invoice.Create(contact.Id, request.SourceKind, request.SourceId, request.SourceLabel, request.IssuedOn, BuyerOf(contact) with { TaxId = TaxIds.Anonymous },
                lines, request.Note);
            if (!created.IsSuccess)
            {
                return null;
            }
        }

        db.Invoices.Add(created.Value!);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Aynı istek eşzamanlı işlendi; benzersiz indeks ikinci faturayı engelledi.
            return null;
        }

        if (settings.AutoSend)
        {
            await IssueAsync(created.Value!.Id, cancellationToken);
        }

        return created.Value!.Id;
    }

    public async Task<Result<Guid>> UpdateDraftAsync(Guid id, InvoiceDraftInput input, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        var updated = invoice.UpdateDraft(input.Buyer, input.Profile, input.Note);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>Alıcı bilgilerini kişi kaydından yeniden okur (taslakta).</summary>
    public async Task<Result<Guid>> RefreshBuyerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null || await contacts.FindBillingAsync(invoice.ContactId, cancellationToken) is not { } contact)
        {
            return InvoiceErrors.NotFound;
        }

        var updated = invoice.UpdateDraft(BuyerOf(contact), invoice.Profile, invoice.Note);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// Faturayı numaralandırıp seçili entegratörle gönderir. Belge kurallara uymuyorsa numara harcanmaz. Entegratör
    /// reddederse fatura numarasıyla taslak kalır ve hata gösterilir; yeniden denemede aynı numara kullanılır.
    /// </summary>
    public async Task<Result<Guid>> IssueAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            return InvoiceErrors.NotDraft;
        }

        var settings = await SettingsAsync(cancellationToken);
        if (!settings.IsSellerComplete)
        {
            return InvoiceErrors.SellerMissing;
        }

        if (SelectedProvider(settings) is not { } provider)
        {
            return InvoiceErrors.NoProvider;
        }

        var today = Today;
        var problems = Builder(invoice, settings, invoice.Number ?? $"{settings.Prefix}{today.Year:D4}{1:D9}", today).Problems();
        if (problems.Count > 0)
        {
            return new Error("invoices.invalid", "Fatura eksik veya hatalı: " + string.Join(", ", problems) + ".");
        }

        if (invoice.Number is null || !invoice.Number.StartsWith($"{settings.Prefix}{today.Year:D4}", StringComparison.Ordinal))
        {
            invoice.PrepareToSend($"{settings.Prefix}{today.Year:D4}{await db.NextNumberAsync(settings.Prefix, today.Year, cancellationToken):D9}", today);
        }
        else
        {
            invoice.PrepareToSend(invoice.Number, today);
        }

        // Numara gönderimden önce kaydedilir: entegratör yanıt vermeden süreç kesilse de numara tekrar kullanılmaz.
        await db.SaveChangesAsync(cancellationToken);

        var document = Builder(invoice, settings, invoice.Number!, today).Build();
        var result = await gateway.SendAsync(provider.Key, document, cancellationToken);
        if (!result.Success)
        {
            invoice.MarkFailed(provider.Key, result.Error ?? "Fatura gönderilemedi.");
            await db.SaveChangesAsync(cancellationToken);
            return new Error("invoices.send_failed", invoice.LastError!);
        }

        invoice.MarkSent(provider.Key, result, document.Xml, clock.GetUtcNow());
        outbox.Enroll(db);
        await outbox.PublishAsync(new RecordTimelineEntry([invoice.ContactId], ActivityKind.System, $"Fatura kesildi: {invoice.Number}", Money(invoice.Total),
            clock.GetUtcNow(), SourceModule, $"invoice:{invoice.Id}"));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return id;
    }

    /// <summary>GİB portalına elle yüklenen faturayı iletildi olarak işaretler.</summary>
    public async Task<Result<Guid>> MarkTransmittedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        var marked = invoice.MarkTransmitted(clock.GetUtcNow());
        if (!marked.IsSuccess)
        {
            return marked.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>Numarasız taslağı siler, numaralı faturayı iptal edildi olarak işaretler. Sonuç true ise fatura silindi.</summary>
    public async Task<Result<bool>> CancelAsync(Guid id, string? reason, CancellationToken cancellationToken = default)
    {
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (invoice is null)
        {
            return InvoiceErrors.NotFound;
        }

        var cancelled = invoice.Cancel(reason);
        if (!cancelled.IsSuccess)
        {
            return cancelled.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return cancelled.Value;
    }

    /// <summary>Kesinleşmiş faturanın UBL-TR dosyası; taslakta önizleme olarak o anki hali üretilir.</summary>
    public async Task<(string FileName, string Xml)?> XmlAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await GetAsync(id, cancellationToken);
        if (invoice is null)
        {
            return null;
        }

        if (invoice.Xml is { } xml)
        {
            return ($"{invoice.Number}.xml", xml);
        }

        var settings = await SettingsAsync(cancellationToken);
        var builder = Builder(invoice, settings, invoice.Number ?? $"{settings.Prefix}{Today.Year:D4}{0:D9}", Today);
        return builder.Problems().Count == 0 ? ($"taslak-{invoice.Id:N}.xml", builder.Build().Xml) : null;
    }

    public async Task<InvoiceSettings> SettingsAsync(CancellationToken cancellationToken = default) =>
        await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new InvoiceSettings();

    public async Task<Result<bool>> SaveSettingsAsync(InvoiceSettings input, CancellationToken cancellationToken = default)
    {
        var prefix = input.Prefix?.Trim().ToUpperInvariant() ?? "";
        if (prefix.Length != 3 || !prefix.All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c)))
        {
            return new Error("invoice_settings.prefix", "Fatura ön eki 3 karakter olmalı (büyük harf veya rakam).");
        }

        var taxId = input.SellerTaxId?.Trim() ?? "";
        if (taxId.Length > 0 && !TaxIds.IsValid(taxId))
        {
            return new Error("invoice_settings.tax_id", "Kurumun VKN'si (10 hane) veya TCKN'si (11 hane) geçerli değil.");
        }

        if (input.DuesVatRate is < 0 or > 100)
        {
            return new Error("invoice_settings.vat", "Aidat KDV oranı 0-100 arasında olmalı.");
        }

        if (input.Provider is { Length: > 0 } key && gateway.Find(key) is null)
        {
            return new Error("invoice_settings.provider", "Seçilen entegratör bu kurulumda yok.");
        }

        var settings = await db.Settings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new InvoiceSettings();
            db.Settings.Add(settings);
        }

        settings.Prefix = prefix;
        settings.SellerName = input.SellerName?.Trim() ?? "";
        settings.SellerTaxId = taxId;
        settings.SellerTaxOffice = Clean(input.SellerTaxOffice);
        settings.SellerStreet = Clean(input.SellerStreet);
        settings.SellerDistrict = Clean(input.SellerDistrict);
        settings.SellerCity = Clean(input.SellerCity);
        settings.SellerPostalCode = Clean(input.SellerPostalCode);
        settings.SellerEmail = Clean(input.SellerEmail);
        settings.SellerPhone = Clean(input.SellerPhone);
        settings.SellerWebsite = Clean(input.SellerWebsite);
        settings.Provider = string.IsNullOrWhiteSpace(input.Provider) ? null : input.Provider.Trim();
        settings.FromQuotes = input.FromQuotes;
        settings.FromDues = input.FromDues;
        settings.AutoSend = input.AutoSend;
        settings.DuesVatRate = input.DuesVatRate;
        settings.ExemptionCode = Clean(input.ExemptionCode) ?? InvoiceSettings.DefaultExemptionCode;
        settings.ExemptionReason = Clean(input.ExemptionReason) ?? InvoiceSettings.DefaultExemptionReason;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<EInvoiceProviderStatus>> ProvidersAsync(CancellationToken cancellationToken = default)
    {
        var settings = await SettingsAsync(cancellationToken);
        var selected = SelectedProvider(settings)?.Key;
        return [.. gateway.Providers.Select(p => new EInvoiceProviderStatus(p, p.Key == selected))];
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        await db.Invoices.IgnoreQueryFilters().Where(i => i.TenantId == db.TenantId && i.ContactId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.ContactId, to), cancellationToken);
    }

    public static string Money(decimal amount) => amount.ToString("N2", Turkish) + " TL";

    /// <summary>Ayarda seçilen entegratör; seçilmemişse ya da ayarı eksikse ayarı tamam olan ilk entegratör.</summary>
    private EInvoiceProviderInfo? SelectedProvider(InvoiceSettings settings) =>
        (settings.Provider is { } key ? gateway.Find(key) : null) is { IsConfigured: true } chosen ? chosen : gateway.Providers.FirstOrDefault(p => p.IsConfigured);

    private EInvoiceDocumentBuilder Builder(Invoice invoice, InvoiceSettings settings, string number, DateOnly today)
    {
        var builder = EInvoiceDocumentBuilder.Create(number, invoice.Uuid)
            .Profile(invoice.Profile)
            .IssuedOn(invoice.Status == InvoiceStatus.Draft ? today : invoice.IssuedOn, TimeOnly.FromDateTime(clock.GetLocalNow().DateTime))
            .Seller(settings.Seller)
            .Buyer(new EInvoiceParty(invoice.BuyerName, invoice.BuyerTaxId)
            {
                FirstName = invoice.BuyerFirstName,
                LastName = invoice.BuyerLastName,
                TaxOffice = invoice.BuyerTaxOffice,
                Street = invoice.BuyerStreet,
                District = invoice.BuyerDistrict,
                City = invoice.BuyerCity,
                PostalCode = invoice.BuyerPostalCode,
                Email = invoice.BuyerEmail,
                Phone = invoice.BuyerPhone,
            })
            .Exemption(settings.ExemptionCode, settings.ExemptionReason)
            .OrderReference(invoice.SourceLabel)
            .Note(invoice.Note);
        foreach (var line in invoice.Lines.OrderBy(l => l.Position))
        {
            builder.Line(line.Description, line.Quantity, line.Unit, line.UnitPrice, line.DiscountRate, line.VatRate);
        }

        return builder;
    }

    private static InvoiceBuyer BuyerOf(ContactBillingInfo c)
    {
        var isPerson = c.Type == ContactType.Person;
        var taxId = c.NationalId is { Length: > 0 } id ? id : TaxIds.Anonymous;
        return new InvoiceBuyer(c.DisplayName, isPerson ? c.FirstName : null, isPerson ? c.LastName : null, taxId, null, c.Street, c.District, c.City, c.PostalCode,
            c.Email, c.Phone);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
