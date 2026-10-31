using Arma.Modules.Automation.Infrastructure;
using Arma.Modules.Automation.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Automation.Application;

internal sealed class AutomationPersonalData(AutomationDbContext db) : IPersonalDataSource
{
    public int Order => 290;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Otomasyon", "Kişiye hangi otomasyon kuralının ne zaman uygulandığı", "İşlem güvenliği", "Aynı işlemin tekrarlanmaması", "Meşru menfaat (KVKK 5/2-f)", "Kural silinene kadar"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var runs = await db.Runs.AsNoTracking().Where(r => r.ContactId == contactId)
            .Join(db.Rules.IgnoreQueryFilters(), r => r.RuleId, rule => rule.Id, (r, rule) => new { rule.Name, r.Status, r.RanAt })
            .OrderBy(r => r.RanAt).ToListAsync(cancellationToken);
        return
        [
            new("Otomasyon", [.. runs.Select(r => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Kural"] = r.Name,
                ["Sonuç"] = AutomationLabels.Status(r.Status),
                ["Tarih"] = r.RanAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            })]),
        ];
    }

    /// <summary>Kayıt yalnızca tekrarı önlemek için tutulur; anonimleştirilen kişi bir daha kurallara girmez, kayıt kalabilir.</summary>
    public Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken) => Task.CompletedTask;
}
