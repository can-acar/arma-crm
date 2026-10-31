using Arma.Modules.Invoicing.Domain;
using Arma.Modules.Invoicing.Infrastructure;
using Arma.Modules.Invoicing.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Invoicing.Application;

/// <summary>Faturadaki alıcı bilgileri kişisel veridir; kesilmiş fatura vergi mevzuatı gereği saklanır.</summary>
internal sealed class InvoicingPersonalData(InvoicingDbContext db) : IPersonalDataSource
{
    public int Order => 295;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Faturalar", "Alıcı adı, T.C. kimlik veya vergi numarası, adres, e-posta, telefon, fatura kalemleri", "Kimlik, iletişim, finans",
            "e-Fatura ve e-Arşiv fatura düzenlenmesi", "Kanunlarda açıkça öngörülme (VUK 213, 3065)", "Fatura yılını izleyen yıldan itibaren 10 yıl"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var rows = await db.Invoices.AsNoTracking().Where(i => i.ContactId == contactId).OrderBy(i => i.IssuedOn).ToListAsync(cancellationToken);
        return
        [
            new("Faturalar", [.. rows.Select(i => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Numara"] = i.Number,
                ["Tarih"] = i.IssuedOn.ToString("dd.MM.yyyy"),
                ["Durum"] = InvoiceLabels.Status(i.Status),
                ["Alıcı"] = i.BuyerName,
                ["Kimlik no"] = i.BuyerTaxId,
                ["Adres"] = string.Join(", ", new[] { i.BuyerStreet, i.BuyerDistrict, i.BuyerCity }.Where(p => p is not null)),
                ["E-posta"] = i.BuyerEmail,
                ["Tutar"] = InvoiceService.Money(i.Total),
            })]),
        ];
    }

    /// <summary>Numarası olmayan taslaklar silinir; numaralı faturalar yasal saklama süresince kalır.</summary>
    public async Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken)
    {
        foreach (var draft in await db.Invoices.Where(i => i.ContactId == contactId && i.Number == null && i.Status == InvoiceStatus.Draft).ToListAsync(cancellationToken))
        {
            draft.Cancel(null);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
