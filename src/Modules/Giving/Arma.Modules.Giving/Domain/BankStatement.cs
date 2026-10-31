using Arma.SharedKernel.Domain;

namespace Arma.Modules.Giving.Domain;

/// <summary>İçe aktarılan banka hesap hareketleri dosyası.</summary>
public sealed class BankStatement : Entity
{
    private BankStatement()
    {
    }

    public string FileName { get; private set; } = "";

    /// <summary>"csv", "xlsx" veya "mt940".</summary>
    public string Format { get; private set; } = "";

    /// <summary>Dosyadaki gelen (alacak) hareket sayısı.</summary>
    public int CreditCount { get; private set; }

    /// <summary>Daha önce içe aktarıldığı için atlanan hareket sayısı.</summary>
    public int DuplicateCount { get; private set; }

    public static BankStatement Create(string fileName, string format, int creditCount, int duplicateCount) =>
        new() { FileName = fileName.Length > 200 ? fileName[..200] : fileName, Format = format, CreditCount = creditCount, DuplicateCount = duplicateCount };
}

public enum BankLineStatus
{
    Open = 1,
    Matched = 2,
    Ignored = 3,
}

/// <summary>
/// Hesaba gelen tek havale/EFT. Bağışçısı onaylanınca bağış kaydına dönüşür. Öneri, aynı IBAN'dan
/// daha önce eşleşmiş harekete veya açıklamadaki ada göre yapılır; hiçbir öneri kendiliğinden kaydedilmez.
/// </summary>
public sealed class BankLine : Entity
{
    private BankLine()
    {
    }

    public Guid StatementId { get; private set; }

    /// <summary>Aynı hareketin ikinci kez içe aktarılmasını önleyen parmak izi.</summary>
    public string Key { get; private set; } = "";

    public DateOnly BookedOn { get; private set; }

    public decimal Amount { get; private set; }

    public string? Description { get; private set; }

    public string? Counterparty { get; private set; }

    public string? Iban { get; private set; }

    public string? BankReference { get; private set; }

    public BankLineStatus Status { get; private set; }

    public Guid? SuggestedContactId { get; private set; }

    public Guid? SuggestedFundId { get; private set; }

    /// <summary>Önerinin nedeni: "IBAN" veya "Ad".</summary>
    public string? SuggestionReason { get; private set; }

    public Guid? ContactId { get; private set; }

    public Guid? DonationId { get; private set; }

    internal static BankLine Create(Guid statementId, string key, StatementRow row) => new()
    {
        StatementId = statementId,
        Key = key,
        BookedOn = row.BookedOn,
        Amount = row.Amount,
        Description = Clip(row.Description, 500),
        Counterparty = Clip(row.Counterparty, 200),
        Iban = Clip(row.Iban, 34),
        BankReference = Clip(row.Reference, 100),
        Status = BankLineStatus.Open,
    };

    internal void Suggest(Guid? contactId, Guid? fundId, string? reason)
    {
        SuggestedContactId = contactId;
        SuggestedFundId = fundId;
        SuggestionReason = contactId is null ? null : reason;
    }

    public Result<bool> Match(Guid contactId, Guid donationId)
    {
        if (Status != BankLineStatus.Open)
        {
            return new Error("bank.not_open", "Bu hareket zaten işlenmiş.");
        }

        (Status, ContactId, DonationId) = (BankLineStatus.Matched, contactId, donationId);
        return true;
    }

    public Result<bool> Ignore()
    {
        if (Status != BankLineStatus.Open)
        {
            return new Error("bank.not_open", "Bu hareket zaten işlenmiş.");
        }

        Status = BankLineStatus.Ignored;
        return true;
    }

    public Result<bool> Reopen()
    {
        if (Status != BankLineStatus.Ignored)
        {
            return new Error("bank.not_ignored", "Yalnızca yok sayılan hareket geri alınabilir.");
        }

        Status = BankLineStatus.Open;
        return true;
    }

    internal void ChangeContact(Guid from, Guid to)
    {
        ContactId = ContactId == from ? to : ContactId;
        SuggestedContactId = SuggestedContactId == from ? to : SuggestedContactId;
    }

    private static string? Clip(string? text, int max)
    {
        text = text?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length > max ? text[..max] : text;
    }
}

/// <summary>Ekstreden okunan satır. <see cref="Amount"/> gelen paralarda pozitif, gidenlerde negatiftir.</summary>
public sealed record StatementRow(DateOnly BookedOn, decimal Amount, string? Description, string? Counterparty, string? Iban, string? Reference);
