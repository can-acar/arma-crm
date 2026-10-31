using Arma.SharedKernel.Domain;

namespace Arma.Modules.Giving.Domain;

public enum PledgeFrequency
{
    /// <summary>Tek seferde ödenecek taahhüt.</summary>
    Once = 0,
    Monthly = 1,
    Quarterly = 3,
    Yearly = 12,
}

public enum PledgeStatus
{
    Active = 1,
    Cancelled = 2,
}

/// <summary>Bir taksidin vadesi, tutarı ve bu taksite düşen ödeme.</summary>
public sealed record PledgeInstallment(int Number, DateOnly DueOn, decimal Amount, decimal Paid)
{
    public bool IsPaid => Paid >= Amount;
}

/// <summary>
/// Bağışçının ileride ödemeyi üstlendiği tutar ("12 ay boyunca ayda 1.000 TL"). Taksitler saklanmaz; tutar,
/// sayı ve sıklıktan hesaplanır. Ödemeler taahhüde bağlanan bağışlardır ve vade sırasıyla taksitlere dağıtılır.
/// </summary>
public sealed class Pledge : Entity
{
    public const int MaxInstallments = 120;

    private Pledge()
    {
    }

    public Guid ContactId { get; private set; }

    public Guid? FundId { get; private set; }

    /// <summary>Taahhüdün toplam tutarı.</summary>
    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = Donation.DefaultCurrency;

    public PledgeFrequency Frequency { get; private set; }

    public int InstallmentCount { get; private set; } = 1;

    public DateOnly StartOn { get; private set; }

    public PledgeStatus Status { get; private set; } = PledgeStatus.Active;

    public string? Note { get; private set; }

    public string? CancelReason { get; private set; }

    public static Result<Pledge> Create(Guid contactId, decimal amount, string? currency, Guid? fundId, PledgeFrequency frequency, int installmentCount, DateOnly startOn, string? note)
    {
        var pledge = new Pledge { ContactId = contactId };
        var result = pledge.Update(amount, currency, fundId, frequency, installmentCount, startOn, note);
        return result.IsSuccess ? pledge : result.Error!;
    }

    public Result<bool> Update(decimal amount, string? currency, Guid? fundId, PledgeFrequency frequency, int installmentCount, DateOnly startOn, string? note)
    {
        if (amount <= 0 || decimal.Round(amount, 2) != amount)
        {
            return new Error("pledges.amount", "Tutar sıfırdan büyük ve en fazla iki ondalık basamaklı olmalı.");
        }

        installmentCount = frequency == PledgeFrequency.Once ? 1 : installmentCount;
        if (installmentCount is < 1 or > MaxInstallments)
        {
            return new Error("pledges.installments", $"Taksit sayısı 1-{MaxInstallments} arasında olmalı.");
        }

        if (amount / installmentCount < 0.01m)
        {
            return new Error("pledges.installments", "Taksit tutarı 0,01'den küçük olamaz.");
        }

        currency = string.IsNullOrWhiteSpace(currency) ? Donation.DefaultCurrency : currency.Trim().ToUpperInvariant();
        if (currency.Length != 3)
        {
            return new Error("pledges.currency", "Para birimi üç harfli kod olmalı (TRY, USD, EUR).");
        }

        Amount = amount;
        Currency = currency;
        FundId = fundId;
        Frequency = frequency;
        InstallmentCount = installmentCount;
        StartOn = startOn;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 1000)];
        return true;
    }

    public Result<bool> Cancel(string? reason)
    {
        if (Status == PledgeStatus.Cancelled)
        {
            return true;
        }

        Status = PledgeStatus.Cancelled;
        CancelReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(reason.Trim().Length, 300)];
        return true;
    }

    public void Reactivate()
    {
        Status = PledgeStatus.Active;
        CancelReason = null;
    }

    public void ChangeContact(Guid contactId) => ContactId = contactId;

    /// <summary>Taksit planı: tutar eşit bölünür, kuruş farkı son taksite eklenir; ödenen tutar vade sırasıyla dağıtılır.</summary>
    public IReadOnlyList<PledgeInstallment> Schedule(decimal paid)
    {
        var share = decimal.Floor(Amount / InstallmentCount * 100) / 100;
        var list = new List<PledgeInstallment>(InstallmentCount);
        for (var i = 0; i < InstallmentCount; i++)
        {
            var amount = i == InstallmentCount - 1 ? Amount - (share * (InstallmentCount - 1)) : share;
            var applied = Math.Clamp(paid, 0, amount);
            paid -= applied;
            list.Add(new PledgeInstallment(i + 1, DueOn(i), amount, applied));
        }

        return list;
    }

    /// <summary>Verilen güne kadar vadesi gelmiş taksitlerin toplamı.</summary>
    public decimal DueBy(DateOnly date) => Schedule(0).Where(i => i.DueOn <= date).Sum(i => i.Amount);

    private DateOnly DueOn(int index) => Frequency == PledgeFrequency.Once ? StartOn : StartOn.AddMonths(index * (int)Frequency);
}

public static class PledgeErrors
{
    public static readonly Error NotFound = new("pledges.not_found", "Taahhüt bulunamadı.");
    public static readonly Error NotForContact = new("pledges.contact", "Taahhüt bu bağışçıya ait değil.");
    public static readonly Error NotActive = new("pledges.not_active", "İptal edilmiş taahhüde ödeme bağlanamaz.");
    public static readonly Error CurrencyMismatch = new("pledges.currency_mismatch", "Bağışın para birimi taahhüdünkiyle aynı olmalı.");
}
