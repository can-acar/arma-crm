using Arma.Infrastructure.Messaging;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Grants.Domain;
using Arma.Modules.Grants.Infrastructure;
using Arma.Modules.Identity.Contracts;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Grants.Application;

public sealed record GrantFilter(GrantStatus? Status = null, Guid? FunderId = null, Guid? OwnerId = null, int? Year = null, string? Search = null);

public sealed record GrantListItem(
    Guid Id,
    string Title,
    string? Program,
    Guid FunderId,
    string FunderName,
    string Currency,
    decimal Requested,
    decimal? Awarded,
    decimal Received,
    GrantStatus Status,
    DateOnly? Deadline,
    DateOnly? StartOn,
    DateOnly? EndOn,
    Guid? OwnerId,
    string? OwnerName,
    DateOnly? NextDueOn,
    string? NextDueTitle,
    bool HasOverdue);

public sealed record GrantMilestoneView(Guid Id, MilestoneKind Kind, string Title, DateOnly DueOn, decimal? Amount, DateOnly? CompletedOn, bool IsOverdue);

public sealed record GrantDetail(GrantListItem Summary, string? Description, DateOnly? SubmittedOn, DateOnly? DecidedOn, IReadOnlyList<GrantMilestoneView> Milestones, DateTimeOffset CreatedAt);

public sealed record SaveGrantRequest(Guid FunderId, string Title, string? Program, string? Currency, decimal Requested, DateOnly? Deadline, DateOnly? StartOn, DateOnly? EndOn, Guid? OwnerId, string? Description);

public sealed record SaveMilestoneRequest(MilestoneKind Kind, string Title, DateOnly DueOn, decimal? Amount);

/// <summary>Pano ve liste özeti. Tutarlar yalnızca TL hibeleri içerir.</summary>
public sealed record GrantSummary(int Pending, decimal PendingRequestedTry, decimal AwardedThisYearTry, decimal ExpectedTry, int DueSoon, int Overdue);

public sealed class GrantService(
    GrantsDbContext db,
    IContactDirectory contacts,
    IUserDirectory users,
    IDbContextOutbox outbox,
    TimeProvider clock)
{
    internal const string SourceModule = "Grants";

    /// <summary>Panoda "yaklaşan" sayılan gün sayısı.</summary>
    public const int DueSoonDays = 30;

    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IReadOnlyList<GrantListItem>> ListAsync(GrantFilter filter, CancellationToken cancellationToken = default)
    {
        var query = db.Grants.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(g => g.Status == status);
        }

        if (filter.FunderId is { } funder)
        {
            query = query.Where(g => g.FunderId == funder);
        }

        if (filter.OwnerId is { } owner)
        {
            query = query.Where(g => g.OwnerId == owner);
        }

        if (filter.Year is { } year)
        {
            var (start, end) = (new DateOnly(year, 1, 1), new DateOnly(year, 12, 31));
            query = query.Where(g => (g.Deadline >= start && g.Deadline <= end) || (g.StartOn <= end && (g.EndOn == null || g.EndOn >= start))
                || (g.Deadline == null && g.StartOn == null && g.CreatedAt.Year == year));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(g => EF.Functions.ILike(g.Title, pattern) || (g.Program != null && EF.Functions.ILike(g.Program, pattern)));
        }

        var rows = await query.OrderBy(g => g.Status == GrantStatus.Closed || g.Status == GrantStatus.Rejected)
            .ThenBy(g => g.Deadline ?? g.StartOn).ThenByDescending(g => g.CreatedAt).Take(1000).ToListAsync(cancellationToken);
        return await ToItemsAsync(rows, cancellationToken);
    }

    public async Task<GrantDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var grant = await db.Grants.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (grant is null)
        {
            return null;
        }

        var today = Today;
        var milestones = grant.Milestones.OrderBy(m => m.DueOn).ThenBy(m => m.CreatedAt)
            .Select(m => new GrantMilestoneView(m.Id, m.Kind, m.Title, m.DueOn, m.Amount, m.CompletedOn, IsOverdue(grant, m, today)))
            .ToList();
        return new GrantDetail((await ToItemsAsync([grant], cancellationToken))[0], grant.Description, grant.SubmittedOn, grant.DecidedOn, milestones, grant.CreatedAt);
    }

    public async Task<GrantSummary> SummaryAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        var yearStart = new DateOnly(today.Year, 1, 1);
        var grants = await db.Grants.AsNoTracking().Where(g => g.Status != GrantStatus.Rejected
            && (g.Status != GrantStatus.Closed || g.DecidedOn >= yearStart)).ToListAsync(cancellationToken);
        var pending = grants.Where(g => g.Status is GrantStatus.Preparing or GrantStatus.Submitted).ToList();
        var awarded = grants.Where(g => g.Awarded is not null && g.DecidedOn >= yearStart && g.Currency == "TRY").Sum(g => g.Awarded!.Value);
        var active = grants.Where(g => g.Status is GrantStatus.Preparing or GrantStatus.Submitted or GrantStatus.Awarded).ToList();
        var open = active.SelectMany(g => g.Milestones.Where(m => !m.IsDone).Select(m => (Grant: g, Milestone: m))).ToList();
        var expected = active.Where(g => g.Status == GrantStatus.Awarded && g.Currency == "TRY")
            .Sum(g => g.Milestones.Where(m => m.Kind == MilestoneKind.Payment && !m.IsDone).Sum(m => m.Amount ?? 0));
        return new GrantSummary(
            pending.Count,
            pending.Where(g => g.Currency == "TRY").Sum(g => g.Requested),
            awarded,
            expected,
            open.Count(x => x.Milestone.DueOn >= today && x.Milestone.DueOn <= today.AddDays(DueSoonDays))
                + pending.Count(g => g.Status == GrantStatus.Preparing && g.Deadline >= today && g.Deadline <= today.AddDays(DueSoonDays)),
            open.Count(x => IsOverdue(x.Grant, x.Milestone, today)));
    }

    public async Task<Result<Guid>> CreateAsync(SaveGrantRequest request, CancellationToken cancellationToken = default)
    {
        if (await ValidateAsync(request, null, cancellationToken) is { } error)
        {
            return error;
        }

        var created = Grant.Create(request.FunderId, request.Title, request.Program, request.Currency, request.Requested, request.Deadline, request.StartOn, request.EndOn, request.OwnerId, request.Description);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        var grant = created.Value!;
        db.Grants.Add(grant);
        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(grant, $"Hibe başvurusu: {grant.Title}", grant.Program, "created"));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return grant.Id;
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, SaveGrantRequest request, CancellationToken cancellationToken = default)
    {
        var grant = await db.Grants.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (grant is null)
        {
            return GrantErrors.NotFound;
        }

        if (await ValidateAsync(request, grant, cancellationToken) is { } error)
        {
            return error;
        }

        var updated = grant.Update(request.Title, request.Program, request.Currency, request.Requested, request.Deadline, request.StartOn, request.EndOn, request.OwnerId, request.Description);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        grant.ChangeFunder(request.FunderId);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> ChangeStatusAsync(Guid id, GrantStatus status, decimal? awarded, CancellationToken cancellationToken = default)
    {
        var grant = await db.Grants.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (grant is null)
        {
            return GrantErrors.NotFound;
        }

        if (grant.Status == status && awarded == grant.Awarded)
        {
            return id;
        }

        var changed = grant.ChangeStatus(status, awarded, Today);
        if (!changed.IsSuccess)
        {
            return changed.Error!;
        }

        outbox.Enroll(db);
        var subject = status switch
        {
            GrantStatus.Submitted => $"Hibe başvurusu yapıldı: {grant.Title}",
            GrantStatus.Awarded => $"Hibe kabul edildi: {grant.Title} ({Money(grant.Awarded!.Value, grant.Currency)})",
            GrantStatus.Rejected => $"Hibe başvurusu reddedildi: {grant.Title}",
            GrantStatus.Closed => $"Hibe projesi tamamlandı: {grant.Title}",
            _ => null,
        };
        if (subject is not null)
        {
            await outbox.PublishAsync(Timeline(grant, subject, null, $"status:{status}"));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var grant = await db.Grants.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (grant is null)
        {
            return GrantErrors.NotFound;
        }

        db.Grants.Remove(grant);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> AddMilestoneAsync(Guid grantId, SaveMilestoneRequest request, CancellationToken cancellationToken = default)
    {
        var grant = await db.Grants.FirstOrDefaultAsync(g => g.Id == grantId, cancellationToken);
        if (grant is null)
        {
            return GrantErrors.NotFound;
        }

        var created = GrantMilestone.Create(grantId, request.Kind, request.Title, request.DueOn, request.Amount);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        db.Milestones.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value!.Id;
    }

    /// <summary>Teslimi yapıldı ya da ödeme alındı olarak işaretler; ödeme alınınca hibe verenin zaman çizelgesine düşer.</summary>
    public async Task<Result<Guid>> SetMilestoneDoneAsync(Guid milestoneId, bool done, CancellationToken cancellationToken = default)
    {
        var milestone = await db.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId, cancellationToken);
        var grant = milestone is null ? null : await db.Grants.FirstOrDefaultAsync(g => g.Id == milestone.GrantId, cancellationToken);
        if (milestone is null || grant is null)
        {
            return GrantErrors.MilestoneNotFound;
        }

        if (done)
        {
            milestone.Complete(Today);
        }
        else
        {
            milestone.Reopen();
        }

        outbox.Enroll(db);
        var key = $"milestone:{milestone.Id}";
        await outbox.PublishAsync<object>(done
            ? Timeline(grant, milestone.Kind == MilestoneKind.Payment
                ? $"Hibe ödemesi alındı: {Money(milestone.Amount ?? 0, grant.Currency)} ({grant.Title})"
                : $"Hibe teslimi yapıldı: {milestone.Title} ({grant.Title})", null, key)
            : new RemoveTimelineEntry(SourceModule, $"{grant.Id}:{key}"));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return milestoneId;
    }

    public async Task<bool> DeleteMilestoneAsync(Guid milestoneId, CancellationToken cancellationToken = default)
    {
        var milestone = await db.Milestones.FirstOrDefaultAsync(m => m.Id == milestoneId, cancellationToken);
        if (milestone is null)
        {
            return false;
        }

        db.Milestones.Remove(milestone);
        outbox.Enroll(db);
        await outbox.PublishAsync(new RemoveTimelineEntry(SourceModule, $"{milestone.GrantId}:milestone:{milestone.Id}"));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<UserSummary>> AssignableUsersAsync(CancellationToken cancellationToken = default) =>
        await users.ActiveUsersAsync(cancellationToken);

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        foreach (var grant in await db.Grants.Where(g => g.FunderId == from).ToListAsync(cancellationToken))
        {
            grant.ChangeFunder(to);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal static string Money(decimal amount, string currency) =>
        string.Format(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"), "{0:N2} {1}", amount, currency == "TRY" ? "TL" : currency);

    /// <summary>Karara bağlanmamış ya da kabul edilmiş hibede tamamlanmamış, günü geçmiş takvim kaydı.</summary>
    private static bool IsOverdue(Grant grant, GrantMilestone milestone, DateOnly today) =>
        !milestone.IsDone && milestone.DueOn < today && grant.Status is GrantStatus.Preparing or GrantStatus.Submitted or GrantStatus.Awarded;

    private async Task<Error?> ValidateAsync(SaveGrantRequest request, Grant? current, CancellationToken cancellationToken)
    {
        if (await contacts.FindAsync(request.FunderId, cancellationToken) is not { } funder || funder.Id != request.FunderId)
        {
            return GrantErrors.FunderNotFound;
        }

        if (request.OwnerId is { } owner && owner != current?.OwnerId
            && (await users.ActiveUsersAsync(cancellationToken)).All(u => u.Id != owner))
        {
            return GrantErrors.OwnerNotFound;
        }

        return null;
    }

    private async Task<IReadOnlyList<GrantListItem>> ToItemsAsync(IReadOnlyList<Grant> rows, CancellationToken cancellationToken)
    {
        var today = Today;
        var funders = await contacts.FindManyAsync([.. rows.Select(r => r.FunderId).Distinct()], cancellationToken);
        var owners = await users.FindManyAsync([.. rows.Where(r => r.OwnerId is not null).Select(r => r.OwnerId!.Value).Distinct()], cancellationToken);
        return [.. rows.Select(g =>
        {
            var next = g.Milestones.Where(m => !m.IsDone).OrderBy(m => m.DueOn).FirstOrDefault();
            DateOnly? nextDue = g.Status == GrantStatus.Preparing && g.Deadline is { } deadline && (next is null || deadline <= next.DueOn) ? deadline : next?.DueOn;
            var nextTitle = nextDue is null ? null : nextDue == g.Deadline && g.Status == GrantStatus.Preparing ? "Başvuru son günü" : next?.Title;
            return new GrantListItem(g.Id, g.Title, g.Program, g.FunderId,
                funders.TryGetValue(g.FunderId, out var funder) ? funder.DisplayName : "Silinmiş kayıt",
                g.Currency, g.Requested, g.Awarded,
                g.Milestones.Where(m => m.Kind == MilestoneKind.Payment && m.IsDone).Sum(m => m.Amount ?? 0),
                g.Status, g.Deadline, g.StartOn, g.EndOn, g.OwnerId,
                g.OwnerId is { } o && owners.TryGetValue(o, out var user) ? user.FullName : null,
                g.Status is GrantStatus.Rejected or GrantStatus.Closed ? null : nextDue, nextTitle,
                g.Milestones.Any(m => IsOverdue(g, m, today)) || (g.Status == GrantStatus.Preparing && g.Deadline < today));
        })];
    }

    private RecordTimelineEntry Timeline(Grant grant, string subject, string? body, string suffix) =>
        new([grant.FunderId], ActivityKind.System, subject, body, clock.GetUtcNow(), SourceModule, $"{grant.Id}:{suffix}");
}
