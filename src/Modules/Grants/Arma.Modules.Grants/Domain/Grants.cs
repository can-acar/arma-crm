using Arma.SharedKernel.Domain;

namespace Arma.Modules.Grants.Domain;

public enum GrantStatus
{
    /// <summary>Başvuru hazırlanıyor ya da fırsat değerlendiriliyor.</summary>
    Preparing = 1,
    Submitted = 2,
    Awarded = 3,
    Rejected = 4,

    /// <summary>Proje bitti, son rapor teslim edildi.</summary>
    Closed = 5,
}

public enum MilestoneKind
{
    /// <summary>Ara veya final raporu, belge teslimi.</summary>
    Report = 1,

    /// <summary>Hibe verenden beklenen ödeme dilimi.</summary>
    Payment = 2,
    Other = 9,
}

/// <summary>Bir vakıf, ajans veya kurumdan (hibe veren) istenen ya da alınan hibe.</summary>
public sealed class Grant : Entity
{
    private Grant()
    {
    }

    /// <summary>Hibe veren kişi kartı (çoğunlukla kurum).</summary>
    public Guid FunderId { get; private set; }

    public string Title { get; private set; } = "";

    /// <summary>Hibe programı veya çağrı adı ("Sivil Toplum Destek Programı 2026").</summary>
    public string? Program { get; private set; }

    public string Currency { get; private set; } = "TRY";

    public decimal Requested { get; private set; }

    public decimal? Awarded { get; private set; }

    public GrantStatus Status { get; private set; } = GrantStatus.Preparing;

    /// <summary>Başvurunun son günü.</summary>
    public DateOnly? Deadline { get; private set; }

    public DateOnly? SubmittedOn { get; private set; }

    public DateOnly? DecidedOn { get; private set; }

    /// <summary>Proje dönemi.</summary>
    public DateOnly? StartOn { get; private set; }

    public DateOnly? EndOn { get; private set; }

    public Guid? OwnerId { get; private set; }

    public string? Description { get; private set; }

    public List<GrantMilestone> Milestones { get; private set; } = [];

    public static Result<Grant> Create(Guid funderId, string title, string? program, string? currency, decimal requested, DateOnly? deadline, DateOnly? startOn, DateOnly? endOn, Guid? ownerId, string? description)
    {
        var grant = new Grant { FunderId = funderId };
        var result = grant.Update(title, program, currency, requested, deadline, startOn, endOn, ownerId, description);
        return result.IsSuccess ? grant : result.Error!;
    }

    public Result<bool> Update(string title, string? program, string? currency, decimal requested, DateOnly? deadline, DateOnly? startOn, DateOnly? endOn, Guid? ownerId, string? description)
    {
        title = title?.Trim() ?? "";
        if (title.Length is 0 or > 200)
        {
            return new Error("grants.title", "Başlık 1-200 karakter olmalı.");
        }

        if (requested < 0 || decimal.Round(requested, 2) != requested)
        {
            return new Error("grants.requested", "Talep edilen tutar sıfır veya daha büyük, en fazla iki ondalık basamaklı olmalı.");
        }

        if (startOn is { } s && endOn is { } e && e < s)
        {
            return new Error("grants.period", "Proje bitişi başlangıçtan önce olamaz.");
        }

        currency = string.IsNullOrWhiteSpace(currency) ? "TRY" : currency.Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            return new Error("grants.currency", "Para birimi üç harfli kod olmalı (TRY, USD, EUR).");
        }

        Title = title;
        Program = Clean(program, 200);
        Currency = currency;
        Requested = requested;
        Deadline = deadline;
        StartOn = startOn;
        EndOn = endOn;
        OwnerId = ownerId;
        Description = Clean(description, 4000);
        return true;
    }

    /// <summary>Durum geçişi. Kabulde verilen tutar zorunludur; başvuru ve karar tarihleri ilk geçişte yazılır.</summary>
    public Result<bool> ChangeStatus(GrantStatus status, decimal? awarded, DateOnly today)
    {
        if (status == GrantStatus.Awarded)
        {
            if (awarded is not { } amount || amount <= 0 || decimal.Round(amount, 2) != amount)
            {
                return new Error("grants.awarded", "Kabul edilen hibe tutarını girin.");
            }

            Awarded = amount;
        }
        else if (status is GrantStatus.Preparing or GrantStatus.Submitted or GrantStatus.Rejected)
        {
            Awarded = null;
        }
        else if (Awarded is null)
        {
            return new Error("grants.not_awarded", "Yalnızca kabul edilmiş hibe tamamlanabilir.");
        }

        if (status != GrantStatus.Preparing)
        {
            SubmittedOn ??= today;
        }
        else
        {
            SubmittedOn = null;
        }

        DecidedOn = status is GrantStatus.Awarded or GrantStatus.Rejected ? DecidedOn ?? today
            : status == GrantStatus.Closed ? DecidedOn
            : null;
        Status = status;
        return true;
    }

    public void ChangeFunder(Guid funderId) => FunderId = funderId;

    internal void Anonymize() => Description = null;

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}

/// <summary>Hibenin takvimindeki teslim (rapor, belge) ya da beklenen ödeme dilimi.</summary>
public sealed class GrantMilestone : Entity
{
    private GrantMilestone()
    {
    }

    public Guid GrantId { get; private set; }

    public MilestoneKind Kind { get; private set; }

    public string Title { get; private set; } = "";

    public DateOnly DueOn { get; private set; }

    /// <summary>Ödeme diliminin tutarı.</summary>
    public decimal? Amount { get; private set; }

    public DateOnly? CompletedOn { get; private set; }

    public bool IsDone => CompletedOn is not null;

    public static Result<GrantMilestone> Create(Guid grantId, MilestoneKind kind, string title, DateOnly dueOn, decimal? amount)
    {
        title = title?.Trim() ?? "";
        if (title.Length is 0 or > 200)
        {
            return new Error("grant_milestones.title", "Başlık 1-200 karakter olmalı.");
        }

        if (kind == MilestoneKind.Payment && (amount is not { } a || a <= 0 || decimal.Round(a, 2) != a))
        {
            return new Error("grant_milestones.amount", "Ödeme diliminin tutarını girin.");
        }

        return new GrantMilestone { GrantId = grantId, Kind = kind, Title = title, DueOn = dueOn, Amount = kind == MilestoneKind.Payment ? amount : null };
    }

    public void Complete(DateOnly on) => CompletedOn = on;

    public void Reopen() => CompletedOn = null;
}

public static class GrantErrors
{
    public static readonly Error NotFound = new("grants.not_found", "Hibe bulunamadı.");
    public static readonly Error FunderNotFound = new("grants.funder", "Hibe veren kişi veya kurum bulunamadı.");
    public static readonly Error OwnerNotFound = new("grants.owner", "Sorumlu kullanıcı bulunamadı veya pasif.");
    public static readonly Error MilestoneNotFound = new("grant_milestones.not_found", "Takvim kaydı bulunamadı.");
}
