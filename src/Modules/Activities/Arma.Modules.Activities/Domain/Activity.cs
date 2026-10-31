using Arma.Modules.Activities.Contracts;
using Arma.SharedKernel.Domain;

namespace Arma.Modules.Activities.Domain;

public enum ActivityStatus
{
    Open = 1,
    Done = 2,
    Cancelled = 3,
}

/// <summary>
/// Kişiyle ilgili bir olay: not, arama, toplantı, görev ya da başka bir modülün düştüğü kayıt.
/// Görevler açık başlar ve tamamlanır; diğer türler gerçekleşmiş olarak kaydedilir.
/// </summary>
public sealed class Activity : Entity
{
    public const int SubjectMaxLength = 200;

    public const int BodyMaxLength = 4000;

    private readonly List<ActivityParticipant> participants = [];

    private Activity()
    {
    }

    public ActivityKind Kind { get; private set; }

    public string Subject { get; private set; } = "";

    public string? Body { get; private set; }

    public ActivityStatus Status { get; private set; }

    /// <summary>Gerçekleştiği an; görevlerde tamamlandığı an.</summary>
    public DateTimeOffset? OccurredAt { get; private set; }

    public DateTimeOffset? DueAt { get; private set; }

    public Guid? AssignedToUserId { get; private set; }

    public string? SourceModule { get; private set; }

    public string? SourceId { get; private set; }

    public IReadOnlyList<ActivityParticipant> Participants => participants;

    public bool IsTask => Kind == ActivityKind.Task;

    public bool IsSystem => SourceModule is not null;

    public static Result<Activity> Create(
        ActivityKind kind, string subject, string? body, DateTimeOffset? occurredAt, DateTimeOffset? dueAt,
        Guid? assignedTo, IEnumerable<Guid> contactIds, DateTimeOffset now)
    {
        if (kind == ActivityKind.System)
        {
            return new Error("activities.kind", "Sistem kayıtları elle oluşturulamaz.");
        }

        var activity = new Activity { Kind = kind };
        var updated = activity.Update(subject, body, occurredAt, dueAt, assignedTo, now);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        activity.Status = kind == ActivityKind.Task ? ActivityStatus.Open : ActivityStatus.Done;
        activity.SetContacts(contactIds);
        return activity;
    }

    public static Activity FromSource(ActivityKind kind, string subject, string? body, DateTimeOffset occurredAt, string sourceModule, string sourceId, IEnumerable<Guid> contactIds)
    {
        var activity = new Activity
        {
            Kind = kind,
            Status = ActivityStatus.Done,
            SourceModule = sourceModule,
            SourceId = sourceId,
        };
        activity.ApplySource(subject, body, occurredAt, contactIds);
        return activity;
    }

    public void ApplySource(string subject, string? body, DateTimeOffset occurredAt, IEnumerable<Guid> contactIds)
    {
        Subject = Truncate(subject.Trim(), SubjectMaxLength);
        Body = string.IsNullOrWhiteSpace(body) ? null : Truncate(body.Trim(), BodyMaxLength);
        OccurredAt = occurredAt;
        SetContacts(contactIds);
    }

    public Result<bool> Update(string subject, string? body, DateTimeOffset? occurredAt, DateTimeOffset? dueAt, Guid? assignedTo, DateTimeOffset now)
    {
        if (IsSystem)
        {
            return new Error("activities.system", "Sistem kayıtları düzenlenemez.");
        }

        subject = subject?.Trim() ?? "";
        if (subject.Length == 0)
        {
            return new Error("activities.subject_required", "Konu gerekli.");
        }

        if (subject.Length > SubjectMaxLength)
        {
            return new Error("activities.subject_length", $"Konu en fazla {SubjectMaxLength} karakter olabilir.");
        }

        if (body?.Length > BodyMaxLength)
        {
            return new Error("activities.body_length", $"Açıklama en fazla {BodyMaxLength} karakter olabilir.");
        }

        Subject = subject;
        Body = string.IsNullOrWhiteSpace(body) ? null : body.Trim();
        AssignedToUserId = assignedTo;
        if (Kind == ActivityKind.Task)
        {
            DueAt = dueAt;
        }
        else
        {
            OccurredAt = occurredAt ?? OccurredAt ?? now;
        }

        return true;
    }

    public void SetContacts(IEnumerable<Guid> contactIds)
    {
        var wanted = contactIds.Distinct().ToHashSet();
        participants.RemoveAll(p => !wanted.Contains(p.ContactId));
        foreach (var id in wanted.Where(id => participants.All(p => p.ContactId != id)))
        {
            participants.Add(new ActivityParticipant(Id, id));
        }
    }

    /// <summary>Birleştirmede mükerrer kaydın katılımı asıl kayda taşınır.</summary>
    public void ReplaceContact(Guid from, Guid to) =>
        SetContacts(participants.Select(p => p.ContactId == from ? to : p.ContactId).ToList());

    public Result<bool> Complete(DateTimeOffset now)
    {
        if (!IsTask)
        {
            return new Error("activities.not_task", "Yalnızca görevler tamamlanabilir.");
        }

        Status = ActivityStatus.Done;
        OccurredAt = now;
        return true;
    }

    public Result<bool> Reopen()
    {
        if (!IsTask)
        {
            return new Error("activities.not_task", "Yalnızca görevler yeniden açılabilir.");
        }

        Status = ActivityStatus.Open;
        OccurredAt = null;
        return true;
    }

    public Result<bool> Cancel()
    {
        if (!IsTask)
        {
            return new Error("activities.not_task", "Yalnızca görevler iptal edilebilir.");
        }

        Status = ActivityStatus.Cancelled;
        return true;
    }

    public bool IsOverdue(DateTimeOffset now) => IsTask && Status == ActivityStatus.Open && DueAt < now;

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}

/// <summary>Aktivitenin ilgili olduğu kişi veya kurum.</summary>
public sealed class ActivityParticipant : Entity
{
    private ActivityParticipant()
    {
    }

    internal ActivityParticipant(Guid activityId, Guid contactId)
    {
        ActivityId = activityId;
        ContactId = contactId;
    }

    public Guid ActivityId { get; private set; }

    public Guid ContactId { get; private set; }
}
