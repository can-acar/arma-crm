using Arma.SharedKernel.Domain;

namespace Arma.Modules.Giving.Domain;

public enum PaymentMethod
{
    Cash = 1,
    BankTransfer = 2,
    CreditCard = 3,
    Online = 4,
    Check = 5,
    InKind = 6,
    Other = 9,
}

public enum DonationStatus
{
    /// <summary>Çevrim içi ödeme başlatıldı, sonucu bekleniyor.</summary>
    Pending = 1,
    Completed = 2,
    Failed = 3,
    Refunded = 4,
    Cancelled = 5,
}

/// <summary>Bir kişinin ya da kurumun yaptığı tek bağış.</summary>
public sealed class Donation : Entity
{
    public const string DefaultCurrency = "TRY";

    private Donation()
    {
    }

    public Guid ContactId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = DefaultCurrency;

    public DateOnly ReceivedOn { get; private set; }

    public Guid? FundId { get; private set; }

    public PaymentMethod Method { get; private set; }

    public DonationStatus Status { get; private set; }

    /// <summary>Dekont, çek veya işlem numarası.</summary>
    public string? Reference { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Ayni bağışta bağışlanan malın tanımı.</summary>
    public string? InKindDescription { get; private set; }

    public string? ReceiptNumber { get; private set; }

    public DateTimeOffset? ReceiptIssuedAt { get; private set; }

    public string? ReceiptVoidReason { get; private set; }

    /// <summary>Çevrim içi ödemede ödeme kuruluşu (iyzico, PayTR) ve onun işlem kimliği.</summary>
    public string? Provider { get; private set; }

    public string? ProviderPaymentId { get; private set; }

    /// <summary>Eski sistemden aktarıldıysa oradaki kimliği ("civicrm:42"). Makbuzu eski sistemde kesildiği için toplu makbuza girmez.</summary>
    public string? ExternalKey { get; private set; }

    /// <summary>Bağış bir taahhüdün ödemesiyse o taahhüt.</summary>
    public Guid? PledgeId { get; private set; }

    public bool HasReceipt => ReceiptNumber is not null && ReceiptVoidReason is null;

    public static Result<Donation> Record(Guid contactId, decimal amount, string? currency, DateOnly receivedOn, Guid? fundId, PaymentMethod method, string? reference, string? note, string? inKindDescription)
    {
        var donation = new Donation { ContactId = contactId, Status = DonationStatus.Completed };
        var result = donation.Update(amount, currency, receivedOn, fundId, method, reference, note, inKindDescription);
        return result.IsSuccess ? donation : result.Error!;
    }

    /// <summary>Çevrim içi bağış formundan gelen, ödemesi henüz tamamlanmamış kayıt.</summary>
    public static Result<Donation> StartOnline(Guid contactId, decimal amount, Guid? fundId, DateOnly today, string provider)
    {
        var donation = new Donation { ContactId = contactId, Status = DonationStatus.Pending, Provider = provider };
        var result = donation.Update(amount, DefaultCurrency, today, fundId, PaymentMethod.Online, null, null, null);
        return result.IsSuccess ? donation : result.Error!;
    }

    public Result<bool> Update(decimal amount, string? currency, DateOnly receivedOn, Guid? fundId, PaymentMethod method, string? reference, string? note, string? inKindDescription)
    {
        if (amount <= 0)
        {
            return DonationErrors.AmountMustBePositive;
        }

        if (decimal.Round(amount, 2) != amount)
        {
            return new Error("donations.amount_precision", "Tutar en fazla iki ondalık basamak olabilir.");
        }

        if (HasReceipt && (amount != Amount || receivedOn != ReceivedOn))
        {
            return new Error("donations.receipt_issued", "Makbuzu kesilmiş bağışın tutarı ve tarihi değiştirilemez; önce makbuzu iptal edin.");
        }

        currency = string.IsNullOrWhiteSpace(currency) ? DefaultCurrency : currency.Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            return new Error("donations.currency", "Para birimi üç harfli kod olmalı (TRY, USD, EUR).");
        }

        if (method == PaymentMethod.InKind && string.IsNullOrWhiteSpace(inKindDescription))
        {
            return new Error("donations.in_kind_description", "Ayni bağışta bağışlanan malın tanımı gerekli.");
        }

        Amount = amount;
        Currency = currency;
        ReceivedOn = receivedOn;
        FundId = fundId;
        Method = method;
        Reference = Clean(reference, 100);
        Note = Clean(note, 1000);
        InKindDescription = method == PaymentMethod.InKind ? Clean(inKindDescription, 500) : null;
        return true;
    }

    public void ChangeContact(Guid contactId) => ContactId = contactId;

    internal void LinkExternal(string key) => ExternalKey = key;

    public void AssignPledge(Guid? pledgeId) => PledgeId = pledgeId;

    public Result<bool> MarkPaid(string providerPaymentId, decimal paidAmount)
    {
        if (Status == DonationStatus.Completed)
        {
            return true;
        }

        if (Status != DonationStatus.Pending)
        {
            return new Error("donations.not_pending", "Bağış ödeme beklemiyor.");
        }

        if (paidAmount != Amount)
        {
            return new Error("donations.amount_mismatch", "Ödenen tutar bağış tutarıyla uyuşmuyor.");
        }

        Status = DonationStatus.Completed;
        ProviderPaymentId = providerPaymentId;
        return true;
    }

    public void MarkFailed(string? reason)
    {
        if (Status == DonationStatus.Pending)
        {
            Status = DonationStatus.Failed;
            Note = Clean(reason, 1000);
        }
    }

    public Result<bool> Refund(string? reason)
    {
        if (Status != DonationStatus.Completed)
        {
            return new Error("donations.not_completed", "Yalnızca tamamlanmış bağış iade edilebilir.");
        }

        Status = DonationStatus.Refunded;
        if (ReceiptNumber is not null && ReceiptVoidReason is null)
        {
            ReceiptVoidReason = "İade";
        }

        Note = Clean(string.Join(" · ", new[] { Note, reason is null ? null : $"İade: {reason}" }.Where(x => !string.IsNullOrWhiteSpace(x))), 1000);
        return true;
    }

    public Result<bool> IssueReceipt(string number, DateTimeOffset now)
    {
        if (Status != DonationStatus.Completed)
        {
            return new Error("donations.receipt_not_completed", "Makbuz yalnızca tamamlanmış bağış için kesilir.");
        }

        if (HasReceipt)
        {
            return new Error("donations.receipt_exists", "Bu bağışın makbuzu zaten kesilmiş.");
        }

        ReceiptNumber = number;
        ReceiptIssuedAt = now;
        ReceiptVoidReason = null;
        return true;
    }

    public Result<bool> VoidReceipt(string reason)
    {
        if (!HasReceipt)
        {
            return new Error("donations.no_receipt", "İptal edilecek makbuz yok.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return new Error("donations.void_reason", "İptal nedeni gerekli.");
        }

        ReceiptVoidReason = Clean(reason, 300);
        return true;
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }
}

public static class DonationErrors
{
    public static readonly Error NotFound = new("donations.not_found", "Bağış bulunamadı.");
    public static readonly Error AmountMustBePositive = new("donations.amount", "Tutar sıfırdan büyük olmalı.");
    public static readonly Error ContactNotFound = new("donations.contact_not_found", "Bağışçı bulunamadı.");
    public static readonly Error FundNotFound = new("donations.fund_not_found", "Fon bulunamadı veya pasif.");
}
