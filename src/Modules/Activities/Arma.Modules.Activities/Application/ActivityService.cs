using Arma.Modules.Activities.Contracts;
using Arma.Modules.Activities.Domain;
using Arma.Modules.Activities.Infrastructure;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Identity.Contracts;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Activities.Application;

public sealed record ActivityFilter(
    Guid? ContactId = null,
    Guid? AssignedTo = null,
    ActivityKind? Kind = null,
    ActivityStatus? Status = null,
    bool OverdueOnly = false,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record ActivityView(
    Guid Id,
    ActivityKind Kind,
    string Subject,
    string? Body,
    ActivityStatus Status,
    DateTimeOffset? OccurredAt,
    DateTimeOffset? DueAt,
    Guid? AssignedToUserId,
    string? AssignedToName,
    bool IsOverdue,
    string? SourceModule,
    IReadOnlyList<ContactSummary> Contacts,
    string? CreatedBy,
    DateTimeOffset CreatedAt)
{
    public bool IsTask => Kind == ActivityKind.Task;

    public bool IsSystem => SourceModule is not null;

    /// <summary>Listede gösterilen zaman: gerçekleşme, yoksa son tarih, yoksa oluşturulma.</summary>
    public DateTimeOffset When => OccurredAt ?? DueAt ?? CreatedAt;
}

public sealed record ActivityPage(IReadOnlyList<ActivityView> Items, int Total, int Page, int PageSize);

public sealed record SaveActivityRequest(
    ActivityKind Kind,
    string Subject,
    string? Body,
    DateTimeOffset? OccurredAt,
    DateTimeOffset? DueAt,
    Guid? AssignedToUserId,
    IReadOnlyList<Guid> ContactIds);

internal static class ActivityErrors
{
    public static readonly Error NotFound = new("activities.not_found", "Aktivite bulunamadı.");
    public static readonly Error ContactNotFound = new("activities.contact_not_found", "Seçilen kişilerden biri bulunamadı.");
    public static readonly Error UserNotFound = new("activities.user_not_found", "Atanan kullanıcı bulunamadı veya pasif.");
}

public sealed class ActivityService(ActivitiesDbContext db, IContactDirectory contacts, IUserDirectory users, TimeProvider clock)
{
    public async Task<ActivityPage> ListAsync(ActivityFilter filter, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var query = db.Activities.AsNoTracking();
        if (filter.ContactId is { } contactId)
        {
            query = query.Where(a => a.Participants.Any(p => p.ContactId == contactId));
        }

        if (filter.AssignedTo is { } userId)
        {
            query = query.Where(a => a.AssignedToUserId == userId);
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(a => a.Kind == kind);
        }

        if (filter.Status is { } status)
        {
            query = query.Where(a => a.Status == status);
        }

        if (filter.OverdueOnly)
        {
            query = query.Where(a => a.Kind == ActivityKind.Task && a.Status == ActivityStatus.Open && a.DueAt < now);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var pattern = $"%{filter.Search.Trim()}%";
            query = query.Where(a => EF.Functions.ILike(a.Subject, pattern) || (a.Body != null && EF.Functions.ILike(a.Body, pattern)));
        }

        var pageSize = Math.Clamp(filter.PageSize, 1, 200);
        var page = Math.Max(1, filter.Page);
        var total = await query.CountAsync(cancellationToken);

        // Açık görevlerde en yakın son tarih önce; geçmiş kayıtlarda en yenisi önce.
        var ordered = filter.Status == ActivityStatus.Open && filter.Kind == ActivityKind.Task
            ? query.OrderBy(a => a.DueAt == null).ThenBy(a => a.DueAt).ThenBy(a => a.CreatedAt)
            : query.OrderByDescending(a => a.OccurredAt ?? a.DueAt ?? a.CreatedAt);

        var items = await ordered
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(a => a.Participants)
            .ToListAsync(cancellationToken);

        return new ActivityPage(await ToViewsAsync(items, cancellationToken), total, page, pageSize);
    }

    /// <summary>Kişi kartındaki zaman çizelgesi: açık görevler ve geçmiş kayıtlar.</summary>
    public async Task<(IReadOnlyList<ActivityView> OpenTasks, IReadOnlyList<ActivityView> History)> TimelineAsync(Guid contactId, int take = 100, CancellationToken cancellationToken = default)
    {
        var open = await ListAsync(new ActivityFilter(ContactId: contactId, Kind: ActivityKind.Task, Status: ActivityStatus.Open, PageSize: 50), cancellationToken);
        var rows = await db.Activities.AsNoTracking()
            .Where(a => a.Participants.Any(p => p.ContactId == contactId) && a.Status != ActivityStatus.Open)
            .OrderByDescending(a => a.OccurredAt ?? a.DueAt ?? a.CreatedAt)
            .Take(take)
            .Include(a => a.Participants)
            .ToListAsync(cancellationToken);
        return (open.Items, await ToViewsAsync(rows, cancellationToken));
    }

    public async Task<ActivityView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var activity = await db.Activities.AsNoTracking().Include(a => a.Participants).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        return activity is null ? null : (await ToViewsAsync([activity], cancellationToken))[0];
    }

    public async Task<Result<Guid>> CreateAsync(SaveActivityRequest request, CancellationToken cancellationToken = default)
    {
        var check = await ValidateReferencesAsync(request, cancellationToken);
        if (check is not null)
        {
            return check;
        }

        var created = Activity.Create(request.Kind, request.Subject, request.Body, request.OccurredAt, request.DueAt,
            request.AssignedToUserId, request.ContactIds, clock.GetUtcNow());
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        db.Activities.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value!.Id;
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, SaveActivityRequest request, CancellationToken cancellationToken = default)
    {
        var activity = await db.Activities.Include(a => a.Participants).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (activity is null)
        {
            return ActivityErrors.NotFound;
        }

        var check = await ValidateReferencesAsync(request, cancellationToken);
        if (check is not null)
        {
            return check;
        }

        var updated = activity.Update(request.Subject, request.Body, request.OccurredAt, request.DueAt, request.AssignedToUserId, clock.GetUtcNow());
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        activity.SetContacts(request.ContactIds);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public Task<Result<Guid>> CompleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, a => a.Complete(clock.GetUtcNow()), cancellationToken);

    public Task<Result<Guid>> ReopenAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, a => a.Reopen(), cancellationToken);

    public Task<Result<Guid>> CancelAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, a => a.Cancel(), cancellationToken);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var activity = await db.Activities.Include(a => a.Participants).FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (activity is null)
        {
            return false;
        }

        db.Participants.RemoveRange(activity.Participants);
        db.Activities.Remove(activity);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Başka bir modülün yayımladığı zaman çizelgesi kaydı; aynı kaynak tekrar gelirse günceller.</summary>
    internal async Task RecordAsync(RecordTimelineEntry entry, CancellationToken cancellationToken)
    {
        var existing = await db.Activities.Include(a => a.Participants)
            .FirstOrDefaultAsync(a => a.SourceModule == entry.SourceModule && a.SourceId == entry.SourceId, cancellationToken);
        if (existing is null)
        {
            db.Activities.Add(Activity.FromSource(entry.Kind, entry.Subject, entry.Body, entry.OccurredAt, entry.SourceModule, entry.SourceId, entry.ContactIds));
        }
        else
        {
            existing.ApplySource(entry.Subject, entry.Body, entry.OccurredAt, entry.ContactIds);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal async Task RemoveSourceAsync(string sourceModule, string sourceId, CancellationToken cancellationToken)
    {
        var existing = await db.Activities.Include(a => a.Participants)
            .FirstOrDefaultAsync(a => a.SourceModule == sourceModule && a.SourceId == sourceId, cancellationToken);
        if (existing is null)
        {
            return;
        }

        db.Participants.RemoveRange(existing.Participants);
        db.Activities.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Kişiler birleştirildiğinde mükerrer kaydın aktivitelerini asıl kayda taşır.</summary>
    internal async Task MoveContactAsync(Guid fromContactId, Guid toContactId, CancellationToken cancellationToken)
    {
        var activities = await db.Activities.Include(a => a.Participants)
            .Where(a => a.Participants.Any(p => p.ContactId == fromContactId))
            .ToListAsync(cancellationToken);
        foreach (var activity in activities)
        {
            activity.ReplaceContact(fromContactId, toContactId);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Result<Guid>> ChangeAsync(Guid id, Func<Activity, Result<bool>> change, CancellationToken cancellationToken)
    {
        var activity = await db.Activities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (activity is null)
        {
            return ActivityErrors.NotFound;
        }

        var result = change(activity);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    private async Task<Error?> ValidateReferencesAsync(SaveActivityRequest request, CancellationToken cancellationToken)
    {
        var contactIds = request.ContactIds.Distinct().ToList();
        if (contactIds.Count > 0)
        {
            var found = await contacts.FindManyAsync(contactIds, cancellationToken);
            if (contactIds.Any(id => !found.ContainsKey(id)))
            {
                return ActivityErrors.ContactNotFound;
            }
        }

        if (request.AssignedToUserId is { } userId)
        {
            var user = await users.FindManyAsync([userId], cancellationToken);
            if (!user.TryGetValue(userId, out var summary) || !summary.IsActive)
            {
                return ActivityErrors.UserNotFound;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<ActivityView>> ToViewsAsync(IReadOnlyList<Activity> items, CancellationToken cancellationToken)
    {
        var contactIds = items.SelectMany(a => a.Participants).Select(p => p.ContactId).Distinct().ToList();
        var userIds = items.Select(a => a.AssignedToUserId).OfType<Guid>().Distinct().ToList();
        var contactMap = await contacts.FindManyAsync(contactIds, cancellationToken);
        var userMap = await users.FindManyAsync(userIds, cancellationToken);
        var now = clock.GetUtcNow();

        return items.Select(a => new ActivityView(
                a.Id, a.Kind, a.Subject, a.Body, a.Status, a.OccurredAt, a.DueAt, a.AssignedToUserId,
                a.AssignedToUserId is { } u && userMap.TryGetValue(u, out var user) ? user.FullName : null,
                a.IsOverdue(now), a.SourceModule,
                a.Participants.Select(p => contactMap.GetValueOrDefault(p.ContactId)).OfType<ContactSummary>().OrderBy(c => c.DisplayName).ToList(),
                a.CreatedBy, a.CreatedAt))
            .ToList();
    }
}
