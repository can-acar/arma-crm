using Arma.Modules.Cases.Infrastructure;
using Arma.Modules.Cases.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Cases.Application;

internal sealed class CasesPersonalData(CasesDbContext db, CaseService cases) : IPersonalDataSource
{
    public int Order => 280;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Vakalar", "Vaka konusu, açıklaması, adımları ve sonucu", "Hizmet / başvuru", "Başvuru ve destek süreçlerinin yürütülmesi", "Bir hakkın tesisi (KVKK 5/2-e) veya açık rıza", "Vaka kapandıktan sonra 5 yıl"),
        new("Vakalar", "Vaka notları (gizli notlar dahil; sağlık gibi özel nitelikli veri içerebilir)", "Hizmet / başvuru", "Vaka takibi", "Açık rıza (KVKK 6) veya kanunda öngörülme", "Vaka kapandıktan sonra 5 yıl"),
    ];

    /// <summary>KVKK m.11 kapsamında kişi hakkında işlenen tüm veriler, gizli notlar dahil, verilir.</summary>
    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var list = await cases.ForContactAsync(contactId, cancellationToken);
        var ids = list.Select(c => c.Id).ToList();
        var numbers = list.ToDictionary(c => c.Id, c => c.Number);
        var notes = await db.Notes.AsNoTracking().Where(n => ids.Contains(n.CaseId)).OrderBy(n => n.CreatedAt).ToListAsync(cancellationToken);
        return
        [
            new("Vakalar", [.. list.Select(c => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Numara"] = c.Number,
                ["Tür"] = c.TypeName,
                ["Konu"] = c.Subject,
                ["Durum"] = CaseLabels.Status(c.Status),
                ["Açılış"] = c.OpenedOn.ToString("dd.MM.yyyy"),
                ["Kapanış"] = c.ClosedOn?.ToString("dd.MM.yyyy"),
            })]),
            new("Vaka notları", [.. notes.Select(n => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Vaka"] = numbers.GetValueOrDefault(n.CaseId),
                ["Tarih"] = n.CreatedAt.ToString("dd.MM.yyyy HH:mm"),
                ["Not"] = n.Body,
            })]),
        ];
    }

    /// <summary>Vaka kaydı istatistik için kalır; konu, açıklama, sonuç ve notlar silinir.</summary>
    public async Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var list = await db.Cases.Where(c => c.ContactId == contactId).ToListAsync(cancellationToken);
        var ids = list.Select(c => c.Id).ToList();
        foreach (var item in list)
        {
            item.Anonymize();
        }

        foreach (var note in await db.Notes.Where(n => ids.Contains(n.CaseId)).ToListAsync(cancellationToken))
        {
            note.Anonymize();
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
