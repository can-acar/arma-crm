using Arma.SharedKernel.Domain;

namespace Arma.Modules.Membership.Domain;

/// <summary>Asıl üye, fahri üye, öğrenci üye gibi. Süre ve aidat türe göre belirlenir.</summary>
public sealed class MembershipType : Entity
{
    private MembershipType()
    {
    }

    public string Name { get; private set; } = "";

    public string? Description { get; private set; }

    /// <summary>Bir dönemin ay olarak uzunluğu; 0 ise ömür boyu (yenilenmez).</summary>
    public int DurationMonths { get; private set; }

    /// <summary>Dönem aidatı (TL). 0 ise aidatsız.</summary>
    public decimal Fee { get; private set; }

    /// <summary>Bitişten sonra üyeliğin "ödeme bekliyor" sayıldığı gün sayısı; sonra süresi dolmuş sayılır.</summary>
    public int GraceDays { get; private set; }

    public bool IsActive { get; private set; } = true;

    public bool IsLifetime => DurationMonths == 0;

    public static Result<MembershipType> Create(string name, string? description, int durationMonths, decimal fee, int graceDays)
    {
        var type = new MembershipType();
        var result = type.Update(name, description, durationMonths, fee, graceDays, true);
        return result.IsSuccess ? type : result.Error!;
    }

    public Result<bool> Update(string name, string? description, int durationMonths, decimal fee, int graceDays, bool isActive)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 120)
        {
            return new Error("membership_types.name", "Tür adı 1-120 karakter olmalı.");
        }

        if (durationMonths is < 0 or > 120)
        {
            return new Error("membership_types.duration", "Süre 0 (ömür boyu) ile 120 ay arasında olmalı.");
        }

        if (fee < 0 || fee > 10_000_000)
        {
            return new Error("membership_types.fee", "Aidat tutarı geçerli değil.");
        }

        if (graceDays is < 0 or > 365)
        {
            return new Error("membership_types.grace", "Ek süre 0-365 gün olmalı.");
        }

        Name = name;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DurationMonths = durationMonths;
        Fee = decimal.Round(fee, 2);
        GraceDays = graceDays;
        IsActive = isActive;
        return true;
    }
}

public enum MembershipStatus
{
    /// <summary>Başlangıç tarihi henüz gelmedi.</summary>
    Pending = 1,
    Current = 2,

    /// <summary>Dönem bitti, ek süre içinde; yenilenirse kesintisiz devam eder.</summary>
    Grace = 3,
    Expired = 4,
    Cancelled = 5,
}

/// <summary>Kişinin bir üyelik türündeki üyeliği. Yenilendikçe bitiş tarihi ileri alınır; ödemeler ayrı tutulur.</summary>
public sealed class Member : Entity
{
    private Member()
    {
    }

    public Guid ContactId { get; private set; }

    public Guid TypeId { get; private set; }

    /// <summary>Kurum içinde sırayla verilen üye numarası.</summary>
    public long Number { get; private set; }

    public DateOnly JoinedOn { get; private set; }

    /// <summary>Geçerli dönemin başlangıcı.</summary>
    public DateOnly StartOn { get; private set; }

    /// <summary>Geçerli dönemin son günü; ömür boyu üyelikte boş.</summary>
    public DateOnly? EndOn { get; private set; }

    public DateOnly? CancelledOn { get; private set; }

    public string? CancelReason { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Yenileme hatırlatmasının gönderildiği dönem sonu; aynı dönem için ikinci kez gönderilmez.</summary>
    public DateOnly? ReminderSentFor { get; set; }

    /// <summary>Eski sistemden aktarıldıysa oradaki kimliği ("civicrm:membership-42").</summary>
    public string? ExternalKey { get; private set; }

    public static Member Create(Guid contactId, MembershipType type, long number, DateOnly startOn, string? note) => new()
    {
        ContactId = contactId,
        TypeId = type.Id,
        Number = number,
        JoinedOn = startOn,
        StartOn = startOn,
        EndOn = PeriodEnd(type, startOn),
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
    };

    /// <summary>Eski sistemdeki üyelik: tarihler olduğu gibi alınır, dönem hesaplanmaz.</summary>
    public static Member Imported(Guid contactId, MembershipType type, long number, DateOnly joinedOn, DateOnly startOn, DateOnly? endOn,
        DateOnly? cancelledOn, string? cancelReason, string? note, string externalKey) => new()
    {
        ContactId = contactId,
        TypeId = type.Id,
        Number = number,
        JoinedOn = joinedOn <= startOn ? joinedOn : startOn,
        StartOn = startOn,
        EndOn = endOn,
        CancelledOn = cancelledOn,
        CancelReason = cancelReason,
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        ExternalKey = externalKey,
    };

    public static DateOnly? PeriodEnd(MembershipType type, DateOnly start) =>
        type.IsLifetime ? null : start.AddMonths(type.DurationMonths).AddDays(-1);

    public static MembershipStatus StatusOf(DateOnly? cancelledOn, DateOnly startOn, DateOnly? endOn, int graceDays, DateOnly today) =>
        cancelledOn is not null && cancelledOn <= today ? MembershipStatus.Cancelled
        : startOn > today ? MembershipStatus.Pending
        : endOn is null || today <= endOn ? MembershipStatus.Current
        : today <= endOn.Value.AddDays(graceDays) ? MembershipStatus.Grace
        : MembershipStatus.Expired;

    /// <summary>
    /// Bir dönem daha ekler. Ek süre bitmeden yenilenirse yeni dönem eskisinin ardından başlar (kesintisiz);
    /// sonra yenilenirse bugünden başlar. Sonlandırılmış üyelik yenilenince yeniden açılır.
    /// </summary>
    public Result<(DateOnly Start, DateOnly End)> Renew(MembershipType type, DateOnly today)
    {
        if (type.IsLifetime || EndOn is null)
        {
            return new Error("memberships.lifetime", "Ömür boyu üyelik yenilenmez.");
        }

        var status = StatusOf(CancelledOn, StartOn, EndOn, type.GraceDays, today);
        var start = status is MembershipStatus.Expired or MembershipStatus.Cancelled ? today : EndOn.Value.AddDays(1);
        var end = PeriodEnd(type, start)!.Value;
        if (status is MembershipStatus.Expired or MembershipStatus.Cancelled)
        {
            StartOn = start;
        }

        EndOn = end;
        CancelledOn = null;
        CancelReason = null;
        return (start, end);
    }

    public Result<bool> Cancel(DateOnly on, string? reason)
    {
        if (CancelledOn is not null)
        {
            return new Error("memberships.cancelled", "Üyelik zaten sonlandırılmış.");
        }

        CancelledOn = on;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        return true;
    }

    public void ChangeType(MembershipType type) => TypeId = type.Id;

    public void UpdateNote(string? note) => Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

    public void MoveTo(Guid contactId) => ContactId = contactId;
}

public enum DuesMethod
{
    Cash = 1,
    BankTransfer = 2,
    Card = 3,
    Other = 9,
}

/// <summary>Bir dönemin aidat ödemesi.</summary>
public sealed class DuesPayment : Entity
{
    private DuesPayment()
    {
    }

    public Guid MemberId { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly PaidOn { get; private set; }

    public DuesMethod Method { get; private set; }

    public DateOnly PeriodStart { get; private set; }

    public DateOnly? PeriodEnd { get; private set; }

    public string? Note { get; private set; }

    public static Result<DuesPayment> Create(Guid memberId, decimal amount, DateOnly paidOn, DuesMethod method, DateOnly periodStart, DateOnly? periodEnd, string? note)
    {
        if (amount <= 0 || amount > 10_000_000)
        {
            return new Error("dues.amount", "Aidat tutarı sıfırdan büyük olmalı.");
        }

        return new DuesPayment
        {
            MemberId = memberId,
            Amount = decimal.Round(amount, 2),
            PaidOn = paidOn,
            Method = method,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
        };
    }
}
