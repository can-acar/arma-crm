using Arma.Modules.Activities.Infrastructure;
using Arma.Modules.Activities.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Activities.Application;

internal sealed class ActivitiesPersonalData(ActivitiesDbContext db) : IPersonalDataSource
{
    public int Order => 100;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Aktiviteler", "Not, arama, toplantı ve görev kayıtları", "Müşteri işlem", "Kişiyle yapılan görüşmelerin takibi", "Meşru menfaat", "Kayıt silinene kadar"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var rows = await Mine(contactId).AsNoTracking().OrderBy(a => a.CreatedAt)
            .Select(a => new { a.Kind, a.Subject, a.Body, a.OccurredAt, a.DueAt, a.Status })
            .ToListAsync(cancellationToken);
        return
        [
            new("Aktiviteler", [.. rows.Select(a => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Tür"] = ActivityLabels.Kind(a.Kind),
                ["Konu"] = a.Subject,
                ["Ayrıntı"] = a.Body,
                ["Tarih"] = ActivityLabels.Date(a.OccurredAt ?? a.DueAt),
                ["Durum"] = ActivityLabels.Status(a.Status),
            })]),
        ];
    }

    public async Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken)
    {
        // Yalnızca bu kişiye ait kayıtların metni silinir; başka kişilerle ortak kayıtlarda yalnızca bağı kaldırılır.
        await Mine(contactId).Where(a => a.Participants.All(p => p.ContactId == contactId))
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Subject, "Anonimleştirildi").SetProperty(a => a.Body, (string?)null), cancellationToken);
        await db.Participants.Where(p => p.ContactId == contactId
                && db.Participants.Any(o => o.ActivityId == p.ActivityId && o.ContactId != contactId))
            .ExecuteDeleteAsync(cancellationToken);
    }

    private IQueryable<Domain.Activity> Mine(Guid contactId) =>
        db.Activities.Where(a => a.Participants.Any(p => p.ContactId == contactId));
}
