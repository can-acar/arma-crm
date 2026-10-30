using Arma.Modules.Forms.Domain;
using Arma.Modules.Forms.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Forms.Application;

/// <summary>Kampanyaya web formu bağlanır; dönem içindeki yanıtlar sayılır (başvuru, imza, gönüllü kaydı).</summary>
internal sealed class FormCampaignSource(FormsDbContext db) : ICampaignSource
{
    public string Kind => "form";

    public string Label => "Form";

    public int Order => 40;

    public string? Permission => FormsPermissions.Read;

    public async Task<IReadOnlyList<CampaignItem>> ItemsAsync(CancellationToken cancellationToken) =>
        await db.Forms.AsNoTracking().OrderBy(f => f.Name).Select(f => new CampaignItem(f.Id, f.Name)).ToListAsync(cancellationToken);

    public string Href(Guid id) => $"forms/{id}/submissions";

    public async Task<IReadOnlyList<CampaignMetric>> MetricsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken)
    {
        var submissions = await Submissions(ids, period).Select(s => s.ContactId).ToListAsync(cancellationToken);
        return
        [
            new("Form yanıtı", submissions.Count, "number"),
            new("Yanıtlayan kişi", submissions.Where(c => c != null).Distinct().Count(), "number"),
        ];
    }

    public async Task<IReadOnlyCollection<Guid>> ContactIdsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken) =>
        await Submissions(ids, period).Where(s => s.ContactId != null).Select(s => s.ContactId!.Value).Distinct().ToListAsync(cancellationToken);

    private IQueryable<FormSubmission> Submissions(IReadOnlyCollection<Guid> ids, CampaignPeriod period)
    {
        var query = db.Submissions.AsNoTracking().Where(s => ids.Contains(s.FormId));
        if (period.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue));
            query = query.Where(s => s.SubmittedAt >= start);
        }

        if (period.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue));
            query = query.Where(s => s.SubmittedAt < end);
        }

        return query;
    }
}
