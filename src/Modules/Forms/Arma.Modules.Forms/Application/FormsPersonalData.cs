using System.Text.Json;
using Arma.Modules.Forms.Infrastructure;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Forms.Application;

internal sealed class FormsPersonalData(FormsDbContext db) : IPersonalDataSource
{
    public int Order => 400;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Formlar", "Web formu yanıtları ve gönderen IP adresi", "Müşteri işlem", "Başvuru, gönüllülük ve iletişim taleplerinin karşılanması", "Açık rıza veya sözleşmenin kurulması", "2 yıl"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var rows = await db.Submissions.AsNoTracking().Where(s => s.ContactId == contactId)
            .Join(db.Forms.IgnoreQueryFilters(), s => s.FormId, f => f.Id, (s, f) => new { f.Title, s.Values, s.SubmittedAt })
            .OrderBy(x => x.SubmittedAt).ToListAsync(cancellationToken);
        return
        [
            new("Form yanıtları", [.. rows.Select(r =>
            {
                var row = new Dictionary<string, string?> { ["Form"] = r.Title, ["Tarih"] = r.SubmittedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") };
                foreach (var value in JsonSerializer.Deserialize<List<SubmissionValue>>(r.Values, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [])
                {
                    row[value.Label] = value.Value;
                }

                return (IReadOnlyDictionary<string, string?>)row;
            })]),
        ];
    }

    public Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken) =>
        db.Submissions.Where(s => s.ContactId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Values, "[]").SetProperty(x => x.IpAddress, (string?)null), cancellationToken);
}
