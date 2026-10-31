using Arma.SharedKernel.Domain;

namespace Arma.Modules.Cases.Domain;

public enum CaseStatus
{
    Open = 1,

    /// <summary>Kişiden ya da başka bir kurumdan yanıt bekleniyor.</summary>
    OnHold = 2,

    Closed = 3,
}

public enum CasePriority
{
    Low = 1,
    Normal = 2,
    High = 3,
    Urgent = 4,
}

/// <summary>Vaka türünün adım şablonu: vaka açıldıktan <see cref="DueAfterDays"/> gün sonra yapılacak iş.</summary>
public sealed record StepTemplate(string Title, int DueAfterDays);

/// <summary>Vaka türü (sosyal yardım başvurusu, hukuki destek, şikâyet gibi) ve vaka açılınca oluşacak adımlar.</summary>
public sealed class CaseType : Entity
{
    public const int MaxSteps = 30;

    private CaseType()
    {
    }

    public string Name { get; private set; } = "";

    public string? Description { get; private set; }

    public bool IsActive { get; private set; } = true;

    public List<StepTemplate> Steps { get; private set; } = [];

    public static Result<CaseType> Create(string name, string? description, IEnumerable<StepTemplate> steps)
    {
        var type = new CaseType();
        var result = type.Update(name, description, true, steps);
        return result.IsSuccess ? type : result.Error!;
    }

    public Result<bool> Update(string name, string? description, bool isActive, IEnumerable<StepTemplate> steps)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
        {
            return new Error("case_types.name", "Tür adı 1-100 karakter olmalı.");
        }

        var list = steps.Where(s => !string.IsNullOrWhiteSpace(s.Title)).Select(s => s with { Title = s.Title.Trim() }).ToList();
        if (list.Count > MaxSteps)
        {
            return new Error("case_types.steps", $"Bir türde en fazla {MaxSteps} adım olabilir.");
        }

        if (list.Any(s => s.Title.Length > 200 || s.DueAfterDays is < 0 or > 3650))
        {
            return new Error("case_types.steps", "Adım adı en fazla 200 karakter, süre 0-3650 gün olmalı.");
        }

        Name = name;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsActive = isActive;
        Steps = list;
        return true;
    }
}

/// <summary>Bir kişi için açılan vaka. Adımlar türün şablonundan oluşur, sonradan eklenebilir.</summary>
public sealed class Case : Entity
{
    private Case()
    {
    }

    /// <summary>Kurum içinde yıl bazında sıralı numara: "V2026-0007".</summary>
    public string Number { get; private set; } = "";

    public Guid TypeId { get; private set; }

    /// <summary>Vakanın sahibi (danışan, başvuran).</summary>
    public Guid ContactId { get; private set; }

    public string Subject { get; private set; } = "";

    public string? Description { get; private set; }

    public CaseStatus Status { get; private set; } = CaseStatus.Open;

    public CasePriority Priority { get; private set; } = CasePriority.Normal;

    /// <summary>Vakadan sorumlu kullanıcı.</summary>
    public Guid? AssigneeId { get; private set; }

    public DateOnly OpenedOn { get; private set; }

    public DateOnly? ClosedOn { get; private set; }

    /// <summary>Kapatılırken yazılan sonuç ("Yardım yapıldı", "Başvuru reddedildi").</summary>
    public string? Outcome { get; private set; }

    public List<CaseStep> Steps { get; private set; } = [];

    public static Result<Case> Open(string number, CaseType type, Guid contactId, string subject, string? description, CasePriority priority, Guid? assigneeId, DateOnly openedOn)
    {
        var item = new Case { Number = number, TypeId = type.Id, ContactId = contactId, OpenedOn = openedOn };
        var result = item.Update(subject, description, priority, assigneeId);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        foreach (var (step, index) in type.Steps.Select((s, i) => (s, i)))
        {
            item.Steps.Add(CaseStep.Create(item.Id, step.Title, openedOn.AddDays(step.DueAfterDays), index).Value!);
        }

        return item;
    }

    public Result<bool> Update(string subject, string? description, CasePriority priority, Guid? assigneeId)
    {
        subject = subject?.Trim() ?? "";
        if (subject.Length is 0 or > 200)
        {
            return CaseErrors.Subject;
        }

        Subject = subject;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()[..Math.Min(description.Trim().Length, 4000)];
        Priority = priority;
        AssigneeId = assigneeId;
        return true;
    }

    public Result<bool> ChangeStatus(CaseStatus status, string? outcome, DateOnly today)
    {
        if (status == Status)
        {
            return true;
        }

        if (status == CaseStatus.Closed && string.IsNullOrWhiteSpace(outcome))
        {
            return new Error("cases.outcome", "Vakayı kapatırken sonucu yazın.");
        }

        Status = status;
        ClosedOn = status == CaseStatus.Closed ? today : null;
        Outcome = status == CaseStatus.Closed ? outcome!.Trim()[..Math.Min(outcome.Trim().Length, 500)] : null;
        return true;
    }

    public void ChangeContact(Guid contactId) => ContactId = contactId;

    internal void Anonymize()
    {
        Subject = "Anonimleştirilmiş vaka";
        Description = null;
        Outcome = Outcome is null ? null : "Anonimleştirildi";
    }
}

public sealed class CaseStep : Entity
{
    private CaseStep()
    {
    }

    public Guid CaseId { get; private set; }

    public string Title { get; private set; } = "";

    public DateOnly? DueOn { get; private set; }

    public int Order { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? CompletedBy { get; private set; }

    public bool IsDone => CompletedAt is not null;

    public static Result<CaseStep> Create(Guid caseId, string title, DateOnly? dueOn, int order)
    {
        title = title?.Trim() ?? "";
        if (title.Length is 0 or > 200)
        {
            return new Error("case_steps.title", "Adım adı 1-200 karakter olmalı.");
        }

        return new CaseStep { CaseId = caseId, Title = title, DueOn = dueOn, Order = order };
    }

    public void Complete(DateTimeOffset at, string? by)
    {
        CompletedAt = at;
        CompletedBy = by;
    }

    public void Reopen()
    {
        CompletedAt = null;
        CompletedBy = null;
    }
}

/// <summary>Vaka notu. Gizli notlar yalnızca gizli not yetkisi olanlara gösterilir ve zaman çizelgesine düşmez.</summary>
public sealed class CaseNote : Entity
{
    private CaseNote()
    {
    }

    public Guid CaseId { get; private set; }

    public string Body { get; private set; } = "";

    public bool IsConfidential { get; private set; }

    public static Result<CaseNote> Create(Guid caseId, string body, bool isConfidential)
    {
        body = body?.Trim() ?? "";
        if (body.Length is 0 or > 8000)
        {
            return new Error("case_notes.body", "Not 1-8000 karakter olmalı.");
        }

        return new CaseNote { CaseId = caseId, Body = body, IsConfidential = isConfidential };
    }

    internal void Anonymize() => Body = "[anonimleştirildi]";
}

public static class CaseErrors
{
    public static readonly Error NotFound = new("cases.not_found", "Vaka bulunamadı.");
    public static readonly Error TypeNotFound = new("case_types.not_found", "Vaka türü bulunamadı veya pasif.");
    public static readonly Error ContactNotFound = new("cases.contact", "Kişi bulunamadı.");
    public static readonly Error Subject = new("cases.subject", "Konu 1-200 karakter olmalı.");
    public static readonly Error DuplicateType = new("case_types.duplicate", "Bu adla bir vaka türü zaten var.");
    public static readonly Error Closed = new("cases.closed", "Kapalı vakada değişiklik yapılamaz; önce yeniden açın.");
}
