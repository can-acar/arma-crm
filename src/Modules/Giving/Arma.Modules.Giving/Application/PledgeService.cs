using Arma.Infrastructure.Messaging;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Giving.Application;

/// <summary>Taahhüt listesindeki durum süzgeci. Gecikme ve tamamlanma ödemelerden hesaplanır.</summary>
public enum PledgeState
{
    Open = 1,
    Overdue = 2,
    Fulfilled = 3,
    Cancelled = 4,
}

public sealed record PledgeView(
    Guid Id,
    Guid ContactId,
    string ContactName,
    decimal Amount,
    string Currency,
    Guid? FundId,
    string? FundName,
    PledgeFrequency Frequency,
    int InstallmentCount,
    DateOnly StartOn,
    PledgeStatus Status,
    string? Note,
    string? CancelReason,
    decimal Paid,
    decimal Overdue,
    DateOnly? NextDueOn,
    DateTimeOffset CreatedAt)
{
    public decimal Remaining => Math.Max(0, Amount - Paid);

    public PledgeState State => Status == PledgeStatus.Cancelled ? PledgeState.Cancelled
        : Paid >= Amount ? PledgeState.Fulfilled
        : Overdue > 0 ? PledgeState.Overdue
        : PledgeState.Open;
}

public sealed record PledgeDetail(PledgeView Pledge, IReadOnlyList<PledgeInstallment> Schedule, IReadOnlyList<DonationView> Payments);

public sealed record SavePledgeRequest(Guid ContactId, decimal Amount, string? Currency, Guid? FundId, PledgeFrequency Frequency, int InstallmentCount, DateOnly StartOn, string? Note);

public sealed record PledgeTotals(int Open, int Overdue, decimal OverdueTry, decimal RemainingTry);

public sealed class PledgeService(GivingDbContext db, DonationService donations, IContactDirectory contacts, IDbContextOutbox outbox, TimeProvider clock)
{
    internal const string TimelinePrefix = "pledge:";

    public async Task<IReadOnlyList<PledgeView>> ListAsync(PledgeState? state = null, Guid? contactId = null, Guid? fundId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Pledges.AsNoTracking();
        if (contactId is { } contact)
        {
            query = query.Where(p => p.ContactId == contact);
        }

        if (fundId is { } fund)
        {
            query = query.Where(p => p.FundId == fund);
        }

        query = state == PledgeState.Cancelled ? query.Where(p => p.Status == PledgeStatus.Cancelled)
            : state is null ? query
            : query.Where(p => p.Status == PledgeStatus.Active);

        var rows = await query.OrderByDescending(p => p.StartOn).ThenByDescending(p => p.CreatedAt).Take(2000).ToListAsync(cancellationToken);
        var views = await ToViewsAsync(rows, cancellationToken);
        return state is null or PledgeState.Cancelled ? views : views.Where(v => v.State == state).ToList();
    }

    public async Task<PledgeDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var pledge = await db.Pledges.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pledge is null)
        {
            return null;
        }

        var view = (await ToViewsAsync([pledge], cancellationToken))[0];
        var payments = await donations.ListAsync(new DonationFilter(PledgeId: id, PageSize: 500), cancellationToken);
        return new PledgeDetail(view, pledge.Schedule(view.Paid), payments.Items);
    }

    /// <summary>Panodaki özet: açık ve geciken taahhüt sayısı, TL olarak gecikmiş ve kalan tutar.</summary>
    public async Task<PledgeTotals> TotalsAsync(CancellationToken cancellationToken = default)
    {
        var open = await ListAsync(PledgeState.Open, cancellationToken: cancellationToken);
        var overdue = await ListAsync(PledgeState.Overdue, cancellationToken: cancellationToken);
        return new PledgeTotals(open.Count, overdue.Count,
            overdue.Where(p => p.Currency == Donation.DefaultCurrency).Sum(p => p.Overdue),
            open.Concat(overdue).Where(p => p.Currency == Donation.DefaultCurrency).Sum(p => p.Remaining));
    }

    public async Task<Result<Guid>> CreateAsync(SavePledgeRequest request, CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(request, null, cancellationToken) is { } error)
        {
            return error;
        }

        var created = Pledge.Create(request.ContactId, request.Amount, request.Currency, request.FundId, request.Frequency, request.InstallmentCount, request.StartOn, request.Note);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        db.Pledges.Add(created.Value!);
        await SaveWithTimelineAsync(created.Value!, cancellationToken);
        return created.Value!.Id;
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, SavePledgeRequest request, CancellationToken cancellationToken = default)
    {
        var pledge = await db.Pledges.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pledge is null)
        {
            return PledgeErrors.NotFound;
        }

        if (await ValidateAsync(request, pledge, cancellationToken) is { } error)
        {
            return error;
        }

        var updated = pledge.Update(request.Amount, request.Currency, request.FundId, request.Frequency, request.InstallmentCount, request.StartOn, request.Note);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await SaveWithTimelineAsync(pledge, cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> CancelAsync(Guid id, string? reason, CancellationToken cancellationToken = default)
    {
        var pledge = await db.Pledges.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pledge is null)
        {
            return PledgeErrors.NotFound;
        }

        pledge.Cancel(reason);
        await SaveWithTimelineAsync(pledge, cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> ReactivateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var pledge = await db.Pledges.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pledge is null)
        {
            return PledgeErrors.NotFound;
        }

        pledge.Reactivate();
        await SaveWithTimelineAsync(pledge, cancellationToken);
        return id;
    }

    /// <summary>Ödemesi olmayan taahhüt silinebilir; ödemesi olan iptal edilir.</summary>
    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var pledge = await db.Pledges.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (pledge is null)
        {
            return PledgeErrors.NotFound;
        }

        if (await db.Donations.AnyAsync(d => d.PledgeId == id, cancellationToken))
        {
            return new Error("pledges.has_payments", "Ödemesi bağlanmış taahhüt silinemez; iptal edin.");
        }

        db.Pledges.Remove(pledge);
        outbox.Enroll(db);
        await outbox.PublishAsync(new RemoveTimelineEntry(DonationService.SourceModule, TimelinePrefix + id));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return id;
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        var pledges = await db.Pledges.Where(p => p.ContactId == from).ToListAsync(cancellationToken);
        foreach (var pledge in pledges)
        {
            pledge.ChangeContact(to);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Error?> ValidateAsync(SavePledgeRequest request, Pledge? current, CancellationToken cancellationToken)
    {
        if (await contacts.FindAsync(request.ContactId, cancellationToken) is not { } contact || contact.Id != request.ContactId)
        {
            return DonationErrors.ContactNotFound;
        }

        if (request.FundId is { } fundId && fundId != current?.FundId && !await db.Funds.AnyAsync(f => f.Id == fundId && f.IsActive, cancellationToken))
        {
            return DonationErrors.FundNotFound;
        }

        if (current is not null)
        {
            if (current.ContactId != request.ContactId && await db.Donations.AnyAsync(d => d.PledgeId == current.Id, cancellationToken))
            {
                return new Error("pledges.contact_locked", "Ödemesi bağlanmış taahhüdün bağışçısı değiştirilemez.");
            }

            var currency = string.IsNullOrWhiteSpace(request.Currency) ? Donation.DefaultCurrency : request.Currency.Trim().ToUpperInvariant();
            if (currency != current.Currency && await db.Donations.AnyAsync(d => d.PledgeId == current.Id, cancellationToken))
            {
                return new Error("pledges.currency_locked", "Ödemesi bağlanmış taahhüdün para birimi değiştirilemez.");
            }

            current.ChangeContact(request.ContactId);
        }

        return null;
    }

    private async Task SaveWithTimelineAsync(Pledge pledge, CancellationToken cancellationToken)
    {
        outbox.Enroll(db);
        var fund = pledge.FundId is { } fundId ? await db.Funds.IgnoreQueryFilters().Where(f => f.Id == fundId).Select(f => f.Name).FirstOrDefaultAsync(cancellationToken) : null;
        var plan = pledge.Frequency == PledgeFrequency.Once ? "" : $", {pledge.InstallmentCount} taksit";
        var subject = $"Taahhüt: {Money.Format(pledge.Amount, pledge.Currency)}{plan}" + (fund is null ? "" : $" ({fund})");
        if (pledge.Status == PledgeStatus.Cancelled)
        {
            subject = $"İptal edildi · {subject}";
        }

        var at = pledge.CreatedAt == default ? clock.GetUtcNow() : pledge.CreatedAt;
        await outbox.PublishAsync(new RecordTimelineEntry([pledge.ContactId], ActivityKind.System, subject,
            pledge.CancelReason ?? pledge.Note, at, DonationService.SourceModule, TimelinePrefix + pledge.Id));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<PledgeView>> ToViewsAsync(IReadOnlyList<Pledge> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(p => p.Id).ToList();
        var paid = await db.Donations.Where(d => d.PledgeId != null && ids.Contains(d.PledgeId.Value) && d.Status == DonationStatus.Completed)
            .GroupBy(d => d.PledgeId!.Value).Select(g => new { g.Key, Total = g.Sum(d => d.Amount) })
            .ToDictionaryAsync(g => g.Key, g => g.Total, cancellationToken);
        var contactMap = await contacts.FindManyAsync(rows.Select(p => p.ContactId).Distinct().ToList(), cancellationToken);
        var fundIds = rows.Select(p => p.FundId).OfType<Guid>().Distinct().ToList();
        var funds = await db.Funds.IgnoreQueryFilters().Where(f => fundIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, cancellationToken);
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        return rows.Select(p =>
        {
            var total = paid.GetValueOrDefault(p.Id);
            var schedule = p.Schedule(total);
            return new PledgeView(p.Id, p.ContactId, contactMap.TryGetValue(p.ContactId, out var c) ? c.DisplayName : "(silinmiş kayıt)",
                p.Amount, p.Currency, p.FundId, p.FundId is { } f ? funds.GetValueOrDefault(f) : null, p.Frequency, p.InstallmentCount, p.StartOn,
                p.Status, p.Note, p.CancelReason, total, Math.Max(0, p.DueBy(today) - total),
                schedule.FirstOrDefault(i => !i.IsPaid)?.DueOn, p.CreatedAt);
        }).ToList();
    }
}
