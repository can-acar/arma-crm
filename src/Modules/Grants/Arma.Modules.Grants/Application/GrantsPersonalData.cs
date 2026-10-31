using Arma.Modules.Grants.Infrastructure;
using Arma.Modules.Grants.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Grants.Application;

/// <summary>Hibe verenler çoğunlukla kurumdur; bireysel hibe veren için başvurular kişisel veri sayılır.</summary>
internal sealed class GrantsPersonalData(GrantsDbContext db) : IPersonalDataSource
{
    public int Order => 290;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Hibeler", "Hibe verenle yapılan başvuru, tutar, takvim ve açıklama", "Kurumsal ilişkiler", "Hibe başvurusu ve proje raporlaması", "Sözleşmenin ifası / meşru menfaat", "Proje bitiminden sonra 10 yıl"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var rows = await db.Grants.AsNoTracking().Where(g => g.FunderId == contactId).OrderBy(g => g.CreatedAt).ToListAsync(cancellationToken);
        return
        [
            new("Hibeler", [.. rows.Select(g => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Başlık"] = g.Title,
                ["Program"] = g.Program,
                ["Talep"] = GrantService.Money(g.Requested, g.Currency),
                ["Kabul"] = g.Awarded is { } a ? GrantService.Money(a, g.Currency) : null,
                ["Durum"] = GrantLabels.Status(g.Status),
                ["Açıklama"] = g.Description,
            })]),
        ];
    }

    /// <summary>Hibe kaydı mali kayıt olarak kalır; serbest metin açıklama silinir.</summary>
    public async Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken)
    {
        foreach (var grant in await db.Grants.Where(g => g.FunderId == contactId).ToListAsync(cancellationToken))
        {
            grant.Anonymize();
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
