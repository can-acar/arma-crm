using Arma.Infrastructure.Messaging;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Giving.Application;

public sealed record DonationFilter(
    Guid? ContactId = null,
    Guid? FundId = null,
    PaymentMethod? Method = null,
    DonationStatus? Status = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record DonationView(
    Guid Id,
    Guid ContactId,
    string ContactName,
    decimal Amount,
    string Currency,
    DateOnly ReceivedOn,
    Guid? FundId,
    string? FundName,
    PaymentMethod Method,
    DonationStatus Status,
    string? Reference,
    string? Note,
    string? InKindDescription,
    string? ReceiptNumber,
    DateTimeOffset? ReceiptIssuedAt,
    string? ReceiptVoidReason,
    string? Provider,
    string? ProviderPaymentId,
    DateTimeOffset CreatedAt,
    string? CreatedBy)
{
    public bool HasReceipt => ReceiptNumber is not null && ReceiptVoidReason is null;
}

public sealed record CurrencyTotal(string Currency, decimal Amount, int Count);

public sealed record DonationPage(IReadOnlyList<DonationView> Items, int Total, int Page, int PageSize, IReadOnlyList<CurrencyTotal> Totals);

public sealed record SaveDonationRequest(
    Guid ContactId,
    decimal Amount,
    string? Currency,
    DateOnly ReceivedOn,
    Guid? FundId,
    PaymentMethod Method,
    string? Reference,
    string? Note,
    string? InKindDescription);

/// <summary>Bağışçının bir yıldaki tamamlanmış bağışları; vergi indirimi için yıllık özet.</summary>
public sealed record AnnualStatement(int Year, IReadOnlyList<DonationView> Donations, IReadOnlyList<CurrencyTotal> Totals);

public sealed record DonorSummary(IReadOnlyList<CurrencyTotal> Lifetime, IReadOnlyList<CurrencyTotal> ThisYear, DateOnly? FirstGift, DateOnly? LastGift, decimal? LargestTry);

public sealed class DonationService(
    GivingDbContext db,
    IContactDirectory contacts,
    IDbContextOutbox outbox,
    IAuditLog audit,
    TimeProvider clock)
{
    internal const string SourceModule = "Giving";

    public async Task<DonationPage> ListAsync(DonationFilter filter, CancellationToken cancellationToken = default)
    {
        var query = Filter(filter);
        var pageSize = Math.Clamp(filter.PageSize, 1, 500);
        var page = Math.Max(1, filter.Page);
        var total = await query.CountAsync(cancellationToken);
        var totals = await Totals(query.Where(d => d.Status == DonationStatus.Completed), cancellationToken);
        var rows = await query.OrderByDescending(d => d.ReceivedOn).ThenByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new DonationPage(await ToViewsAsync(rows, cancellationToken), total, page, pageSize, totals);
    }

    public async Task<DonationView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var donation = await db.Donations.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        return donation is null ? null : (await ToViewsAsync([donation], cancellationToken))[0];
    }

    public async Task<DonorSummary> DonorSummaryAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var completed = db.Donations.Where(d => d.ContactId == contactId && d.Status == DonationStatus.Completed);
        var yearStart = new DateOnly(clock.GetLocalNow().Year, 1, 1);
        var first = await completed.MinAsync(d => (DateOnly?)d.ReceivedOn, cancellationToken);
        var last = await completed.MaxAsync(d => (DateOnly?)d.ReceivedOn, cancellationToken);
        var largest = await completed.Where(d => d.Currency == Donation.DefaultCurrency).MaxAsync(d => (decimal?)d.Amount, cancellationToken);
        return new DonorSummary(
            await Totals(completed, cancellationToken),
            await Totals(completed.Where(d => d.ReceivedOn >= yearStart), cancellationToken),
            first, last, largest);
    }

    public Task<Result<Guid>> RecordAsync(SaveDonationRequest request, CancellationToken cancellationToken = default) =>
        RecordAsync(request, cancellationToken, null);

    /// <param name="beforeSave">Bağışla aynı kayıt işleminde yapılacak değişiklik (banka hareketini eşleşti işaretlemek gibi).</param>
    internal async Task<Result<Guid>> RecordAsync(SaveDonationRequest request, CancellationToken cancellationToken, Func<Donation, Result<bool>>? beforeSave)
    {
        var check = await ValidateAsync(request.ContactId, request.FundId, null, cancellationToken);
        if (check is not null)
        {
            return check;
        }

        var created = Donation.Record(request.ContactId, request.Amount, request.Currency, request.ReceivedOn, request.FundId,
            request.Method, request.Reference, request.Note, request.InKindDescription);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        if (beforeSave?.Invoke(created.Value!) is { IsSuccess: false } failed)
        {
            return failed.Error!;
        }

        db.Donations.Add(created.Value!);
        await SaveWithTimelineAsync(created.Value!, cancellationToken);
        return created.Value!.Id;
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, SaveDonationRequest request, CancellationToken cancellationToken = default)
    {
        var donation = await db.Donations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (donation is null)
        {
            return DonationErrors.NotFound;
        }

        var check = await ValidateAsync(request.ContactId, request.FundId, donation.FundId, cancellationToken);
        if (check is not null)
        {
            return check;
        }

        if (donation.HasReceipt && donation.ContactId != request.ContactId)
        {
            return new Error("donations.receipt_issued", "Makbuzu kesilmiş bağışın bağışçısı değiştirilemez; önce makbuzu iptal edin.");
        }

        var updated = donation.Update(request.Amount, request.Currency, request.ReceivedOn, request.FundId, request.Method, request.Reference, request.Note, request.InKindDescription);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        donation.ChangeContact(request.ContactId);
        await SaveWithTimelineAsync(donation, cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> RefundAsync(Guid id, string? reason, CancellationToken cancellationToken = default)
    {
        var donation = await db.Donations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (donation is null)
        {
            return DonationErrors.NotFound;
        }

        var refunded = donation.Refund(reason);
        if (!refunded.IsSuccess)
        {
            return refunded.Error!;
        }

        await SaveWithTimelineAsync(donation, cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var donation = await db.Donations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (donation is null)
        {
            return DonationErrors.NotFound;
        }

        if (donation.ReceiptNumber is not null)
        {
            return new Error("donations.has_receipt", "Makbuz kesilmiş bağış silinemez; iade veya makbuz iptali kullanın.");
        }

        db.Donations.Remove(donation);
        outbox.Enroll(db);
        await outbox.PublishAsync(new RemoveTimelineEntry(SourceModule, donation.Id.ToString()));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return id;
    }

    /// <summary>Yıl içinde sıralı makbuz numarası verir: "{önek}2026-000001".</summary>
    public async Task<Result<string>> IssueReceiptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var donation = await db.Donations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (donation is null)
        {
            return DonationErrors.NotFound;
        }

        if (donation.Status != DonationStatus.Completed || donation.HasReceipt)
        {
            var invalid = donation.IssueReceipt("", clock.GetUtcNow());
            return invalid.Error!;
        }

        var now = clock.GetUtcNow();
        var year = clock.GetLocalNow().Year;
        var prefix = await db.ReceiptSettings.Select(s => s.NumberPrefix).FirstOrDefaultAsync(cancellationToken) ?? "";
        var sequence = await db.NextReceiptNumberAsync(year, cancellationToken);
        var number = $"{prefix}{year}-{sequence:D6}";
        donation.IssueReceipt(number, now);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return number;
    }

    /// <summary>Yıl içinde tamamlanmış ama makbuzu kesilmemiş bağış sayısı.</summary>
    public Task<int> PendingReceiptCountAsync(int year, CancellationToken cancellationToken = default) =>
        PendingReceipts(year).CountAsync(cancellationToken);

    /// <summary>Yıl sonu toplu makbuz: makbuzu kesilmemiş tüm bağışlara bağış tarihine göre sırayla makbuz keser. Kesilen sayıyı döner.</summary>
    public async Task<int> IssuePendingReceiptsAsync(int year, CancellationToken cancellationToken = default)
    {
        var ids = await PendingReceipts(year).OrderBy(d => d.ReceivedOn).ThenBy(d => d.CreatedAt).Select(d => d.Id).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            count += (await IssueReceiptAsync(id, cancellationToken)).IsSuccess ? 1 : 0;
        }

        if (count > 0)
        {
            await audit.WriteAsync(AuditAction.Updated, nameof(Donation), year.ToString(System.Globalization.CultureInfo.InvariantCulture), $"{year} yılı için {count} bağışa toplu makbuz kesildi", cancellationToken);
        }

        return count;
    }

    public async Task<AnnualStatement> AnnualStatementAsync(Guid contactId, int year, CancellationToken cancellationToken = default)
    {
        var (start, end) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        var query = db.Donations.AsNoTracking().Where(d => d.ContactId == contactId && d.Status == DonationStatus.Completed && d.ReceivedOn >= start && d.ReceivedOn <= end);
        var rows = await query.OrderBy(d => d.ReceivedOn).ToListAsync(cancellationToken);
        return new(year, await ToViewsAsync(rows, cancellationToken), await Totals(query, cancellationToken));
    }

    private IQueryable<Donation> PendingReceipts(int year)
    {
        var (start, end) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
        return db.Donations.Where(d => d.Status == DonationStatus.Completed && d.ReceivedOn >= start && d.ReceivedOn <= end
            && d.ExternalKey == null && (d.ReceiptNumber == null || d.ReceiptVoidReason != null));
    }

    public async Task<Result<Guid>> VoidReceiptAsync(Guid id, string reason, CancellationToken cancellationToken = default)
    {
        var donation = await db.Donations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (donation is null)
        {
            return DonationErrors.NotFound;
        }

        var voided = donation.VoidReceipt(reason);
        if (!voided.IsSuccess)
        {
            return voided.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(AuditAction.Updated, nameof(Donation), id.ToString(), $"{donation.ReceiptNumber} numaralı makbuz iptal edildi: {reason}", cancellationToken);
        return id;
    }

    public async Task<ReceiptSettings> ReceiptSettingsAsync(CancellationToken cancellationToken = default) =>
        await db.ReceiptSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new ReceiptSettings();

    public async Task SaveReceiptSettingsAsync(ReceiptSettings values, CancellationToken cancellationToken = default)
    {
        var settings = await db.ReceiptSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new ReceiptSettings();
            db.ReceiptSettings.Add(settings);
        }

        settings.OrganizationName = values.OrganizationName.Trim();
        settings.Address = Trim(values.Address);
        settings.LegalInfo = Trim(values.LegalInfo);
        settings.Footer = Trim(values.Footer);
        settings.NumberPrefix = values.NumberPrefix.Trim().ToUpperInvariant();
        await db.SaveChangesAsync(cancellationToken);
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        var donations = await db.Donations.Where(d => d.ContactId == from).ToListAsync(cancellationToken);
        foreach (var donation in donations)
        {
            donation.ChangeContact(to);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Eski sistemden aktarılan bağışları zaman çizelgesi kayıtlarıyla birlikte tek işlemde kaydeder.</summary>
    internal async Task SaveImportedAsync(IReadOnlyList<Donation> donations, IReadOnlyDictionary<Guid, string> fundNames, CancellationToken cancellationToken)
    {
        outbox.Enroll(db);
        db.Donations.AddRange(donations);
        foreach (var donation in donations)
        {
            var fund = donation.FundId is { } fundId ? fundNames.GetValueOrDefault(fundId) : null;
            var text = $"Bağış: {Money.Format(donation.Amount, donation.Currency)}" + (fund is null ? "" : $" ({fund})");
            await outbox.PublishAsync(new RecordTimelineEntry([donation.ContactId], ActivityKind.System,
                donation.Status == DonationStatus.Refunded ? $"İade edildi · {text}" : text,
                donation.Note, ToTimestamp(donation.ReceivedOn), SourceModule, donation.Id.ToString()));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    /// <summary>Kaydı kaydeder ve kişinin zaman çizelgesini aynı işlemde günceller.</summary>
    internal async Task SaveWithTimelineAsync(Donation donation, CancellationToken cancellationToken, object? alsoPublish = null)
    {
        outbox.Enroll(db);
        object message = donation.Status is DonationStatus.Completed or DonationStatus.Refunded
            ? new RecordTimelineEntry([donation.ContactId], ActivityKind.System, await TimelineSubjectAsync(donation, cancellationToken),
                donation.Note, ToTimestamp(donation.ReceivedOn), SourceModule, donation.Id.ToString())
            : new RemoveTimelineEntry(SourceModule, donation.Id.ToString());
        await outbox.PublishAsync(message);
        if (alsoPublish is not null)
        {
            await outbox.PublishAsync(alsoPublish);
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
    }

    private async Task<string> TimelineSubjectAsync(Donation donation, CancellationToken cancellationToken)
    {
        var fund = donation.FundId is { } fundId ? await db.Funds.Where(f => f.Id == fundId).Select(f => f.Name).FirstOrDefaultAsync(cancellationToken) : null;
        var text = $"Bağış: {Money.Format(donation.Amount, donation.Currency)}" + (fund is null ? "" : $" ({fund})");
        return donation.Status == DonationStatus.Refunded ? $"İade edildi · {text}" : text;
    }

    private DateTimeOffset ToTimestamp(DateOnly date)
    {
        // Bugünkü bağış şu anki saatle, geçmiş tarihli bağış günün başıyla sıralanır.
        var now = clock.GetLocalNow();
        return DateOnly.FromDateTime(now.DateTime) == date
            ? now
            : new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), now.Offset);
    }

    private IQueryable<Donation> Filter(DonationFilter filter)
    {
        var query = db.Donations.AsNoTracking();
        if (filter.ContactId is { } contactId)
        {
            query = query.Where(d => d.ContactId == contactId);
        }

        if (filter.FundId is { } fundId)
        {
            query = query.Where(d => d.FundId == fundId);
        }

        if (filter.Method is { } method)
        {
            query = query.Where(d => d.Method == method);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(d => d.Status == status);
        }

        if (filter.From is { } from)
        {
            query = query.Where(d => d.ReceivedOn >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(d => d.ReceivedOn <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(d => (d.ReceiptNumber != null && EF.Functions.ILike(d.ReceiptNumber, pattern))
                || (d.Reference != null && EF.Functions.ILike(d.Reference, pattern))
                || (d.Note != null && EF.Functions.ILike(d.Note, pattern)));
        }

        return query;
    }

    private static async Task<IReadOnlyList<CurrencyTotal>> Totals(IQueryable<Donation> query, CancellationToken cancellationToken)
    {
        var rows = await query.GroupBy(d => d.Currency)
            .Select(g => new { Currency = g.Key, Amount = g.Sum(d => d.Amount), Count = g.Count() })
            .ToListAsync(cancellationToken);
        return rows.OrderBy(t => t.Currency == Donation.DefaultCurrency ? 0 : 1).ThenBy(t => t.Currency)
            .Select(t => new CurrencyTotal(t.Currency, t.Amount, t.Count))
            .ToList();
    }

    private async Task<Error?> ValidateAsync(Guid contactId, Guid? fundId, Guid? currentFundId, CancellationToken cancellationToken)
    {
        if (await contacts.FindAsync(contactId, cancellationToken) is not { } contact || contact.Id != contactId)
        {
            return DonationErrors.ContactNotFound;
        }

        // Pasif fona yeni bağış yazılmaz; mevcut kaydın fonu korunabilir.
        if (fundId is { } id && id != currentFundId && !await db.Funds.AnyAsync(f => f.Id == id && f.IsActive, cancellationToken))
        {
            return DonationErrors.FundNotFound;
        }

        return null;
    }

    private async Task<IReadOnlyList<DonationView>> ToViewsAsync(IReadOnlyList<Donation> rows, CancellationToken cancellationToken)
    {
        var contactMap = await contacts.FindManyAsync(rows.Select(d => d.ContactId).Distinct().ToList(), cancellationToken);
        var fundIds = rows.Select(d => d.FundId).OfType<Guid>().Distinct().ToList();
        var funds = await db.Funds.IgnoreQueryFilters().Where(f => fundIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);
        return rows.Select(d => new DonationView(
                d.Id, d.ContactId, contactMap.TryGetValue(d.ContactId, out var c) ? c.DisplayName : "(silinmiş kayıt)",
                d.Amount, d.Currency, d.ReceivedOn, d.FundId, d.FundId is { } f ? funds.GetValueOrDefault(f) : null,
                d.Method, d.Status, d.Reference, d.Note, d.InKindDescription, d.ReceiptNumber, d.ReceiptIssuedAt, d.ReceiptVoidReason,
                d.Provider, d.ProviderPaymentId, d.CreatedAt, d.CreatedBy))
            .ToList();
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
