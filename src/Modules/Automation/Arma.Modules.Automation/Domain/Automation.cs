using System.Text.Json;
using Arma.SharedKernel.Domain;

namespace Arma.Modules.Automation.Domain;

public enum TriggerKind
{
    /// <summary>Kişi kayıtlı bir aramaya (segmente) ilk kez uyduğunda; düzenli aralıkla denetlenir.</summary>
    SegmentEntered = 1,

    /// <summary>Bir modül kişinin zaman akışına kayıt düştüğünde (bağış alındı, satış kazanıldı gibi).</summary>
    TimelineEvent = 2,
}

public enum ActionKind
{
    AddTag = 1,
    AddToGroup = 2,
    CreateTask = 3,
    SendEmail = 4,
}

/// <param name="DueDays">Görevin son tarihi: kural çalıştıktan kaç gün sonra.</param>
public sealed record RuleAction(ActionKind Kind, string? TagName = null, Guid? GroupId = null, string? Subject = null, string? Body = null, int? DueDays = null, Guid? AssigneeUserId = null);

public sealed record RuleInput(
    string Name, bool IsActive, TriggerKind Trigger, Guid? SegmentId, string? SourceModule, string? SubjectContains, bool IncludeExisting, IReadOnlyList<RuleAction> Actions);

/// <summary>"Şu olunca şunları yap" kuralı. Her kişi için bir olayda en fazla bir kez çalışır.</summary>
public sealed class Rule : Entity
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Zaman akışı tetikleyicisinde seçilebilen kaynaklar. İleti modülü kendi gönderdiği e-postayla döngü kurmasın diye yok.</summary>
    public static readonly string[] Sources = ["Giving", "Membership", "Events", "Sales", "Forms"];

    private Rule()
    {
    }

    public string Name { get; private set; } = "";

    public bool IsActive { get; private set; }

    public TriggerKind Trigger { get; private set; }

    public Guid? SegmentId { get; private set; }

    public string? SourceModule { get; private set; }

    /// <summary>Zaman akışı kaydının konusu bu metni içeriyorsa çalışır ("Satış kazanıldı" gibi).</summary>
    public string? SubjectContains { get; private set; }

    /// <summary>Segment kuralı açıldığında zaten uyan kişilere de uygulanır mı?</summary>
    public bool IncludeExisting { get; private set; }

    /// <summary>Kural açıldı, mevcut uyanlar henüz "atlandı" olarak işaretlenmedi.</summary>
    public bool BaselinePending { get; private set; }

    public string ActionsJson { get; private set; } = "[]";

    public DateTimeOffset? LastRunAt { get; private set; }

    public IReadOnlyList<RuleAction> Actions => JsonSerializer.Deserialize<List<RuleAction>>(ActionsJson, Json) ?? [];

    public static Result<Rule> Create(RuleInput input)
    {
        var rule = new Rule();
        var updated = rule.Update(input);
        return updated.IsSuccess ? rule : updated.Error!;
    }

    public Result<bool> Update(RuleInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return new Error("automation.name", "Kurala bir ad verin.");
        }

        if (input.Trigger == TriggerKind.SegmentEntered && input.SegmentId is null)
        {
            return new Error("automation.segment", "Kuralın izleyeceği kayıtlı aramayı seçin.");
        }

        if (input.Trigger == TriggerKind.TimelineEvent && !Sources.Contains(input.SourceModule))
        {
            return new Error("automation.source", "Kuralı tetikleyecek modülü seçin.");
        }

        if (input.Actions.Count == 0)
        {
            return new Error("automation.no_actions", "En az bir işlem ekleyin.");
        }

        foreach (var action in input.Actions)
        {
            var problem = action.Kind switch
            {
                ActionKind.AddTag when string.IsNullOrWhiteSpace(action.TagName) => "Etiket işleminde etiket adını yazın.",
                ActionKind.AddToGroup when action.GroupId is null => "Gruba ekleme işleminde grubu seçin.",
                ActionKind.CreateTask when string.IsNullOrWhiteSpace(action.Subject) => "Görev işleminde görev konusunu yazın.",
                ActionKind.CreateTask when action.DueDays is < 0 or > 365 => "Görevin süresi 0-365 gün olmalı.",
                ActionKind.SendEmail when string.IsNullOrWhiteSpace(action.Subject) || string.IsNullOrWhiteSpace(action.Body) => "E-posta işleminde konu ve metin gerekli.",
                _ => null,
            };
            if (problem is not null)
            {
                return new Error("automation.action", problem);
            }
        }

        var trigger = input.Trigger;
        var segment = trigger == TriggerKind.SegmentEntered ? input.SegmentId : null;
        if (input.IsActive && (!IsActive || segment != SegmentId))
        {
            BaselinePending = trigger == TriggerKind.SegmentEntered && !input.IncludeExisting;
        }

        Name = input.Name.Trim();
        IsActive = input.IsActive;
        Trigger = trigger;
        SegmentId = segment;
        SourceModule = trigger == TriggerKind.TimelineEvent ? input.SourceModule : null;
        SubjectContains = trigger == TriggerKind.TimelineEvent && !string.IsNullOrWhiteSpace(input.SubjectContains) ? input.SubjectContains.Trim() : null;
        IncludeExisting = trigger == TriggerKind.SegmentEntered && input.IncludeExisting;
        ActionsJson = JsonSerializer.Serialize(input.Actions.Select(Clean).ToList(), Json);
        return true;
    }

    public bool Matches(string sourceModule, string subject) =>
        IsActive && Trigger == TriggerKind.TimelineEvent && SourceModule == sourceModule
        && (SubjectContains is null || subject.Contains(SubjectContains, StringComparison.CurrentCultureIgnoreCase));

    internal void BaselineDone() => BaselinePending = false;

    internal void Ran(DateTimeOffset now) => LastRunAt = now;

    private static RuleAction Clean(RuleAction action) => action.Kind switch
    {
        ActionKind.AddTag => new(action.Kind, TagName: action.TagName!.Trim()),
        ActionKind.AddToGroup => new(action.Kind, GroupId: action.GroupId),
        ActionKind.CreateTask => new(action.Kind, Subject: action.Subject!.Trim(), Body: Trim(action.Body), DueDays: action.DueDays ?? 0, AssigneeUserId: action.AssigneeUserId),
        _ => new(action.Kind, Subject: action.Subject!.Trim(), Body: action.Body!.Trim()),
    };

    private static string? Trim(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

public enum RunStatus
{
    Done = 1,

    /// <summary>Segment kuralı açıldığında zaten uyan kişi; işlem yapılmadı.</summary>
    Skipped = 2,
}

/// <summary>Kuralın bir kişi için çalıştığının kaydı; aynı olayda ikinci kez çalışmayı önler.</summary>
public sealed class RuleRun : Entity
{
    public const string SegmentKey = "segment";

    private RuleRun()
    {
    }

    public Guid RuleId { get; private set; }

    public Guid ContactId { get; private set; }

    /// <summary>Segment kuralında "segment", zaman akışında kaynağın "Modül:Kimlik" değeri.</summary>
    public string Key { get; private set; } = "";

    public RunStatus Status { get; private set; }

    public DateTimeOffset RanAt { get; private set; }

    public static RuleRun Create(Guid ruleId, Guid contactId, string key, RunStatus status, DateTimeOffset now) =>
        new() { RuleId = ruleId, ContactId = contactId, Key = key, Status = status, RanAt = now };

    internal void ChangeContact(Guid contactId) => ContactId = contactId;
}
