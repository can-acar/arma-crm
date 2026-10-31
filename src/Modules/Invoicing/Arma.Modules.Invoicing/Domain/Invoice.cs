using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Invoicing;

namespace Arma.Modules.Invoicing.Domain;

public enum InvoiceStatus
{
    /// <summary>Gönderilmedi; alıcı ve senaryo değiştirilebilir.</summary>
    Draft = 1,

    /// <summary>Numara verildi, belge kesinleşti.</summary>
    Issued = 2,

    Cancelled = 3,
}

/// <summary>Faturadaki alıcı bilgileri; fatura açıldığı andaki kişi kaydından kopyalanır, sonra kişi değişse de fatura değişmez.</summary>
public sealed record InvoiceBuyer(
    string Name, string? FirstName, string? LastName, string TaxId, string? TaxOffice, string? Street, string? District, string? City, string? PostalCode,
    string? Email, string? Phone);

/// <summary>
/// e-Arşiv veya e-Fatura. Taslakta numarası yoktur; gönderilirken GİB biçiminde sıradaki numara (ARM2026000000001) verilir.
/// Gönderim başarısız olursa numara faturada kalır ve yeniden denemede aynı numara kullanılır; böylece sırada boşluk oluşmaz.
/// </summary>
public sealed class Invoice : Entity
{
    private Invoice()
    {
    }

    public string? Number { get; private set; }

    /// <summary>ETTN.</summary>
    public Guid Uuid { get; private set; } = Guid.NewGuid();

    public EInvoiceProfiles Profile { get; private set; } = EInvoiceProfiles.EArsiv;

    public InvoiceStatus Status { get; private set; } = InvoiceStatus.Draft;

    /// <summary>Taslakta kaynağın tarihi; gönderilince fatura tarihi.</summary>
    public DateOnly IssuedOn { get; private set; }

    public Guid ContactId { get; private set; }

    /// <summary>Faturayı doğuran kayıt türü (<c>sales.quote</c>, <c>membership.dues</c>).</summary>
    public string SourceKind { get; private set; } = "";

    public Guid? SourceId { get; private set; }

    public string? SourceLabel { get; private set; }

    public string BuyerName { get; private set; } = "";

    public string? BuyerFirstName { get; private set; }

    public string? BuyerLastName { get; private set; }

    public string BuyerTaxId { get; private set; } = TaxIds.Anonymous;

    public string? BuyerTaxOffice { get; private set; }

    public string? BuyerStreet { get; private set; }

    public string? BuyerDistrict { get; private set; }

    public string? BuyerCity { get; private set; }

    public string? BuyerPostalCode { get; private set; }

    public string? BuyerEmail { get; private set; }

    public string? BuyerPhone { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Gönderen entegratörün anahtarı.</summary>
    public string? Provider { get; private set; }

    /// <summary>Entegratörün faturaya verdiği kimlik.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>Fatura GİB'e iletildi. Dosya hazırlayan sağlayıcıda kullanıcı portala yükleyince işaretler.</summary>
    public DateTimeOffset? TransmittedAt { get; private set; }

    public string? LastError { get; private set; }

    public string? CancelReason { get; private set; }

    /// <summary>Kesinleşmiş UBL-TR belgesi.</summary>
    public string? Xml { get; private set; }

    public List<InvoiceLine> Lines { get; private set; } = [];

    public decimal Subtotal => Lines.Sum(l => l.Net);

    public decimal VatTotal => Lines.Sum(l => l.Vat);

    public decimal Total => Subtotal + VatTotal;

    public static Result<Invoice> Create(Guid contactId, string sourceKind, Guid? sourceId, string? sourceLabel, DateOnly issuedOn, InvoiceBuyer buyer,
        IReadOnlyList<InvoiceLineInput> lines, string? note)
    {
        var invoice = new Invoice { ContactId = contactId, SourceKind = sourceKind, SourceId = sourceId, SourceLabel = sourceLabel?.Trim(), IssuedOn = issuedOn };
        if (lines.Count == 0)
        {
            return InvoiceErrors.NoLines;
        }

        foreach (var line in lines)
        {
            var created = InvoiceLine.Create(line, invoice.Lines.Count + 1);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            invoice.Lines.Add(created.Value!);
        }

        var updated = invoice.UpdateDraft(buyer, EInvoiceProfiles.EArsiv, note);
        return updated.IsSuccess ? invoice : updated.Error!;
    }

    public Result<bool> UpdateDraft(InvoiceBuyer buyer, EInvoiceProfiles profile, string? note)
    {
        if (Status != InvoiceStatus.Draft)
        {
            return InvoiceErrors.NotDraft;
        }

        var name = buyer.Name?.Trim() ?? "";
        if (name.Length is 0 or > 200)
        {
            return new Error("invoices.buyer", "Alıcı adı 1-200 karakter olmalı.");
        }

        var taxId = string.IsNullOrWhiteSpace(buyer.TaxId) ? TaxIds.Anonymous : buyer.TaxId.Trim();
        if (taxId != TaxIds.Anonymous && !TaxIds.IsValid(taxId))
        {
            return new Error("invoices.tax_id", "Alıcının VKN (10 hane) veya TCKN'si (11 hane) geçerli değil.");
        }

        if (profile == EInvoiceProfiles.EFatura && taxId == TaxIds.Anonymous)
        {
            return new Error("invoices.profile", "e-Fatura için alıcının VKN veya TCKN'si gerekir.");
        }

        BuyerName = name;
        BuyerFirstName = Clean(buyer.FirstName);
        BuyerLastName = Clean(buyer.LastName);
        BuyerTaxId = taxId;
        BuyerTaxOffice = Clean(buyer.TaxOffice);
        BuyerStreet = Clean(buyer.Street);
        BuyerDistrict = Clean(buyer.District);
        BuyerCity = Clean(buyer.City);
        BuyerPostalCode = Clean(buyer.PostalCode);
        BuyerEmail = Clean(buyer.Email);
        BuyerPhone = Clean(buyer.Phone);
        Profile = profile is EInvoiceProfiles.EFatura ? EInvoiceProfiles.EFatura : EInvoiceProfiles.EArsiv;
        Note = Clean(note);
        return true;
    }

    public InvoiceBuyer Buyer => new(BuyerName, BuyerFirstName, BuyerLastName, BuyerTaxId, BuyerTaxOffice, BuyerStreet, BuyerDistrict, BuyerCity, BuyerPostalCode,
        BuyerEmail, BuyerPhone);

    /// <summary>Gönderimden önce numara ve fatura tarihi verilir. Numarası olan fatura yeniden denemede aynı numarayı korur.</summary>
    public void PrepareToSend(string number, DateOnly today)
    {
        Number ??= number;
        IssuedOn = today;
    }

    public void MarkSent(string provider, EInvoiceSendResult result, string xml, DateTimeOffset now)
    {
        Status = InvoiceStatus.Issued;
        Provider = provider;
        ProviderReference = result.Reference;
        TransmittedAt = result.Transmitted ? now : null;
        LastError = null;
        Xml = xml;
    }

    public void MarkFailed(string provider, string error)
    {
        Provider = provider;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }

    public Result<bool> MarkTransmitted(DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Issued)
        {
            return InvoiceErrors.NotIssued;
        }

        TransmittedAt ??= now;
        return true;
    }

    /// <summary>
    /// Numarası olmayan taslak silinir (<c>true</c>); numaralı fatura sırada boşluk kalmasın diye silinmez, iptal edildi olarak işaretlenir.
    /// </summary>
    public Result<bool> Cancel(string? reason)
    {
        if (Status == InvoiceStatus.Cancelled)
        {
            return new Error("invoices.cancelled", "Fatura zaten iptal edilmiş.");
        }

        if (Number is null)
        {
            IsDeleted = true;
            return true;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return new Error("invoices.cancel_reason", "Numaralı fatura için iptal nedeni yazılmalı.");
        }

        Status = InvoiceStatus.Cancelled;
        CancelReason = reason.Trim();
        return false;
    }

    public void ChangeContact(Guid contactId) => ContactId = contactId;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <param name="VatRate">KDV oranı (%).</param>
public sealed record InvoiceLineInput(string Description, decimal Quantity, string? Unit, decimal UnitPrice, decimal DiscountRate, int VatRate);

public sealed class InvoiceLine : Entity
{
    private InvoiceLine()
    {
    }

    public Guid InvoiceId { get; private set; }

    public int Position { get; private set; }

    public string Description { get; private set; } = "";

    public decimal Quantity { get; private set; }

    public string? Unit { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal DiscountRate { get; private set; }

    public int VatRate { get; private set; }

    public decimal Net => AsDocumentLine.Net;

    public decimal Vat => AsDocumentLine.Vat;

    public EInvoiceLine AsDocumentLine => new(Position, Description, Quantity, UnitCodes.From(Unit), UnitPrice, DiscountRate, VatRate);

    internal static Result<InvoiceLine> Create(InvoiceLineInput input, int position)
    {
        var description = input.Description?.Trim() ?? "";
        if (description.Length is 0 or > 500)
        {
            return new Error("invoices.line_description", $"{position}. kalemin açıklaması 1-500 karakter olmalı.");
        }

        if (input.Quantity <= 0 || input.UnitPrice < 0 || input.DiscountRate is < 0 or > 100 || input.VatRate is < 0 or > 100)
        {
            return new Error("invoices.line_amount", $"{position}. kalemde miktar, fiyat, iskonto veya KDV geçerli değil.");
        }

        return new InvoiceLine
        {
            Position = position,
            Description = description,
            Quantity = input.Quantity,
            Unit = string.IsNullOrWhiteSpace(input.Unit) ? null : input.Unit.Trim(),
            UnitPrice = EInvoiceMath.Round(input.UnitPrice),
            DiscountRate = input.DiscountRate,
            VatRate = input.VatRate,
        };
    }
}

/// <summary>Satıcı (kurum) bilgileri, entegratör seçimi ve hangi kayıtların faturalanacağı; kurum başına tek kayıt.</summary>
public sealed class InvoiceSettings : Entity
{
    public const string DefaultExemptionCode = "351";
    public const string DefaultExemptionReason = "KDV - İstisna Olmayan Diğer";

    /// <summary>Fatura numarasının 3 karakterlik ön eki (büyük harf veya rakam).</summary>
    public string Prefix { get; set; } = "ARM";

    public string SellerName { get; set; } = "";

    /// <summary>Kurumun VKN'si (şahıs işletmesinde TCKN).</summary>
    public string SellerTaxId { get; set; } = "";

    public string? SellerTaxOffice { get; set; }

    public string? SellerStreet { get; set; }

    public string? SellerDistrict { get; set; }

    public string? SellerCity { get; set; }

    public string? SellerPostalCode { get; set; }

    public string? SellerEmail { get; set; }

    public string? SellerPhone { get; set; }

    public string? SellerWebsite { get; set; }

    /// <summary>Faturaları gönderen entegratör; boşsa ayarı tamam olan ilk entegratör.</summary>
    public string? Provider { get; set; }

    /// <summary>Kabul edilen tekliften taslak fatura açılır.</summary>
    public bool FromQuotes { get; set; } = true;

    /// <summary>Alınan aidattan taslak fatura açılır.</summary>
    public bool FromDues { get; set; } = true;

    /// <summary>Kaynaktan açılan fatura onay beklemeden gönderilir.</summary>
    public bool AutoSend { get; set; }

    /// <summary>Aidat faturasındaki KDV oranı (%).</summary>
    public int DuesVatRate { get; set; }

    /// <summary>%0 KDV'li kalemlerde yazılan GİB istisna kodu.</summary>
    public string ExemptionCode { get; set; } = DefaultExemptionCode;

    public string ExemptionReason { get; set; } = DefaultExemptionReason;

    public bool IsSellerComplete => SellerName.Length > 0 && TaxIds.IsValid(SellerTaxId) && !string.IsNullOrWhiteSpace(SellerTaxOffice) && !string.IsNullOrWhiteSpace(SellerCity);

    public EInvoiceParty Seller => new(SellerName, SellerTaxId)
    {
        TaxOffice = SellerTaxOffice,
        Street = SellerStreet,
        District = SellerDistrict,
        City = SellerCity,
        PostalCode = SellerPostalCode,
        Email = SellerEmail,
        Phone = SellerPhone,
        Website = SellerWebsite,
    };
}

public static class InvoiceErrors
{
    public static readonly Error NotFound = new("invoices.not_found", "Fatura bulunamadı.");
    public static readonly Error NoLines = new("invoices.lines", "Faturada en az bir kalem olmalı.");
    public static readonly Error NotDraft = new("invoices.not_draft", "Gönderilmiş fatura değiştirilemez.");
    public static readonly Error NotIssued = new("invoices.not_issued", "Fatura henüz gönderilmedi.");
    public static readonly Error SellerMissing = new("invoices.seller", "Fatura ayarlarında kurum adı, geçerli VKN, vergi dairesi ve il girilmeli.");
    public static readonly Error NoProvider = new("invoices.provider", "Ayarları tamamlanmış bir e-Fatura entegratörü yok.");
}
