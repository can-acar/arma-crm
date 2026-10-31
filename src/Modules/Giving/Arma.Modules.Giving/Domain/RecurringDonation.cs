using Arma.SharedKernel.Domain;

namespace Arma.Modules.Giving.Domain;

public enum RecurringStatus
{
    /// <summary>İlk ödeme ve kart kaydı bekleniyor.</summary>
    PendingSetup = 1,
    Active = 2,

    /// <summary>Üst üste başarısız tahsilattan sonra durdu; bağışçı veya görevli yeniden başlatabilir.</summary>
    Paused = 3,
    Cancelled = 4,
}

/// <summary>
/// Kayıtlı karttan her ay çekilen düzenli bağış. Kart sağlayıcıda saklanır; burada yalnızca sağlayıcının verdiği,
/// veri koruma anahtarıyla şifrelenmiş jeton tutulur.
/// </summary>
public sealed class RecurringDonation : Entity
{
    public const int MaxFailures = 3;
    public static readonly TimeSpan RetryAfter = TimeSpan.FromDays(3);

    private RecurringDonation()
    {
    }

    public Guid ContactId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = Donation.DefaultCurrency;

    public Guid? FundId { get; private set; }

    public string Provider { get; private set; } = "";

    public RecurringStatus Status { get; private set; }

    /// <summary>Ayın kaçında çekileceği (1–28; 29–31 her ay olmadığı için kullanılmaz).</summary>
    public int DayOfMonth { get; private set; }

    public DateOnly? NextChargeOn { get; private set; }

    /// <summary>Şifreli kart jetonu; düz metin hiçbir zaman saklanmaz.</summary>
    public string? CardToken { get; private set; }

    /// <summary>Gösterim için kart etiketi (ör. "MASTER CARD **** 1234").</summary>
    public string? CardLabel { get; private set; }

    public int FailureCount { get; private set; }

    public string? LastError { get; private set; }

    public DateOnly? LastChargedOn { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public static Result<RecurringDonation> Start(Guid contactId, decimal amount, Guid? fundId, string provider, DateOnly today)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            return DonationErrors.AmountMustBePositive;
        }

        return new RecurringDonation
        {
            ContactId = contactId,
            Amount = amount,
            FundId = fundId,
            Provider = provider,
            Status = RecurringStatus.PendingSetup,
            DayOfMonth = Math.Min(today.Day, 28),
        };
    }

    /// <summary>İlk ödeme başarılı ve kart kaydedildi: bir sonraki ay aynı gün çekilir.</summary>
    public void Activate(string protectedToken, string? label, DateOnly paidOn)
    {
        if (Status != RecurringStatus.PendingSetup)
        {
            return;
        }

        CardToken = protectedToken;
        CardLabel = label?.Length > 60 ? label[..60] : label;
        Status = RecurringStatus.Active;
        LastChargedOn = paidOn;
        NextChargeOn = NextMonth(paidOn);
    }

    /// <summary>İlk ödeme başarısız oldu ya da kart kaydedilmedi.</summary>
    public void AbandonSetup(string reason, DateTimeOffset now)
    {
        if (Status == RecurringStatus.PendingSetup)
        {
            Status = RecurringStatus.Cancelled;
            LastError = Clip(reason);
            CancelledAt = now;
        }
    }

    public bool IsDue(DateOnly today) => Status == RecurringStatus.Active && NextChargeOn is { } next && next <= today;

    public void RecordCharge(DateOnly chargedOn)
    {
        FailureCount = 0;
        LastError = null;
        LastChargedOn = chargedOn;
        NextChargeOn = NextMonth(chargedOn);
    }

    /// <summary>Başarısız tahsilat: birkaç gün sonra yeniden denenir; üst üste <see cref="MaxFailures"/> kez olursa durur.</summary>
    public bool RecordFailure(string error, DateOnly today)
    {
        FailureCount++;
        LastError = Clip(error);
        if (FailureCount >= MaxFailures)
        {
            Status = RecurringStatus.Paused;
            NextChargeOn = null;
            return true;
        }

        NextChargeOn = today.AddDays(RetryAfter.Days);
        return false;
    }

    public Result<bool> Resume(DateOnly today)
    {
        if (Status != RecurringStatus.Paused)
        {
            return new Error("recurring.not_paused", "Yalnızca durmuş düzenli bağış yeniden başlatılabilir.");
        }

        Status = RecurringStatus.Active;
        FailureCount = 0;
        LastError = null;
        NextChargeOn = today;
        return true;
    }

    public Result<bool> Cancel(DateTimeOffset now)
    {
        if (Status == RecurringStatus.Cancelled)
        {
            return true;
        }

        Status = RecurringStatus.Cancelled;
        NextChargeOn = null;
        CardToken = null;
        CancelledAt = now;
        return true;
    }

    private DateOnly NextMonth(DateOnly from)
    {
        var month = from.AddMonths(1);
        return new DateOnly(month.Year, month.Month, Math.Min(DayOfMonth, DateTime.DaysInMonth(month.Year, month.Month)));
    }

    private static string Clip(string value) => value.Length <= 300 ? value : value[..300];
}
