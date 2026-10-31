using Arma.Modules.Sales.Infrastructure;
using Arma.Modules.Sales.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Sales.Application;

internal sealed class SalesPersonalData(SalesDbContext db, SalesService sales, QuoteService quotes) : IPersonalDataSource
{
    public int Order => 270;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Satış", "Satış fırsatı, tutar, aşama, notlar", "Müşteri işlemi", "Satış süreci takibi", "Sözleşmenin kurulması (KVKK 5/2-c)", "Son işlemden sonra 3 yıl"),
        new("Satış", "Teklif kalemleri ve tutarları", "Müşteri işlemi", "Fiyat teklifi", "Sözleşmenin kurulması (KVKK 5/2-c)", "10 yıl (TTK)"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken) =>
    [
        new("Satış fırsatları", [.. (await sales.ForContactAsync(contactId, cancellationToken)).Select(o => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
        {
            ["Fırsat"] = o.Title,
            ["Aşama"] = o.StageName,
            ["Tutar"] = SalesService.Money(o.Amount),
            ["Kapanış"] = o.ClosedOn?.ToString("dd.MM.yyyy"),
            ["Not"] = o.Note,
        })]),
        new("Teklifler", [.. (await quotes.ListAsync(contactId: contactId, cancellationToken: cancellationToken)).Select(q => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
        {
            ["Numara"] = q.Number,
            ["Tarih"] = q.IssuedOn.ToString("dd.MM.yyyy"),
            ["Durum"] = SalesLabels.Status(q.Status),
            ["Toplam"] = SalesService.Money(q.Total),
        })]),
    ];

    /// <summary>Teklif ve tutarlar ticari defter gereği saklanır; serbest metin notları silinir.</summary>
    public Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken) =>
        db.Opportunities.Where(o => o.ContactId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Note, (string?)null).SetProperty(o => o.LostReason, (string?)null), cancellationToken);
}
