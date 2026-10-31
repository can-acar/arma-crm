using Arma.Modules.Giving.Infrastructure;
using Arma.Modules.Giving.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Giving.Application;

internal sealed class GivingPersonalData(GivingDbContext db) : IPersonalDataSource
{
    public int Order => 200;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Bağışlar", "Bağış tutarı, tarihi, ödeme yöntemi, makbuz", "Finans", "Bağış kabulü, makbuz ve muhasebe", "Kanuni yükümlülük (VUK, 5253)", "Makbuz yılından itibaren 10 yıl"),
        new("Bağışlar", "Kart ödemesi işlem numarası", "Finans", "Ödeme mutabakatı ve iade", "Sözleşmenin ifası", "10 yıl; kart numarası tutulmaz"),
        new("Bağışlar", "Havale/EFT hareketi: gönderen adı, IBAN, açıklama", "Finans", "Gelen havalenin bağışçıyla eşleştirilmesi", "Kanuni yükümlülük (VUK, 5253)", "10 yıl"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var rows = await db.Donations.AsNoTracking().Where(d => d.ContactId == contactId).OrderBy(d => d.ReceivedOn)
            .Select(d => new { d.ReceivedOn, d.Amount, d.Currency, d.Method, d.Status, d.ReceiptNumber, Fund = db.Funds.Where(f => f.Id == d.FundId).Select(f => f.Name).FirstOrDefault(), d.Note })
            .ToListAsync(cancellationToken);
        var transfers = await db.BankLines.AsNoTracking().Where(l => l.ContactId == contactId).OrderBy(l => l.BookedOn)
            .Select(l => new { l.BookedOn, l.Amount, l.Counterparty, l.Iban, l.Description }).ToListAsync(cancellationToken);
        return
        [
            new("Havale/EFT hareketleri", [.. transfers.Select(l => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Tarih"] = GivingLabels.Date(l.BookedOn),
                ["Tutar"] = Money.Format(l.Amount, "TRY"),
                ["Gönderen"] = l.Counterparty,
                ["IBAN"] = l.Iban,
                ["Açıklama"] = l.Description,
            })]),
            new("Bağışlar", [.. rows.Select(d => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Tarih"] = GivingLabels.Date(d.ReceivedOn),
                ["Tutar"] = Money.Format(d.Amount, d.Currency),
                ["Fon"] = d.Fund ?? "Genel",
                ["Yöntem"] = GivingLabels.Method(d.Method),
                ["Durum"] = GivingLabels.Status(d.Status),
                ["Makbuz"] = d.ReceiptNumber,
                ["Not"] = d.Note,
            })]),
        ];
    }

    /// <summary>Bağış ve makbuz kanunen saklanır; yalnızca serbest metin notu ve havalenin gönderen bilgileri silinir. Bağışçı adı kişi kaydından geldiği için anonim görünür.</summary>
    public async Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken)
    {
        await db.Donations.Where(d => d.ContactId == contactId && d.Note != null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.Note, (string?)null), cancellationToken);
        await db.BankLines.Where(l => l.ContactId == contactId || l.SuggestedContactId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Description, (string?)null).SetProperty(l => l.Counterparty, (string?)null)
                .SetProperty(l => l.Iban, (string?)null).SetProperty(l => l.SuggestedContactId, (Guid?)null), cancellationToken);
    }
}
