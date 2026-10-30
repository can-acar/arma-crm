using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Privacy.Domain;
using Arma.Modules.Privacy.Infrastructure;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Privacy.Application;

internal sealed class PrivacyInsights(PrivacyDbContext db, DataRequestService requests) : IContactCriteriaProvider, IDashboardContributor
{
    public async Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken)
    {
        var notices = await db.Notices.AsNoTracking().Where(n => n.RequiresConsent && n.CurrentVersion > 0).OrderBy(n => n.Title)
            .Select(n => new CriterionOption(n.Id.ToString(), n.Title)).ToListAsync(cancellationToken);
        return notices.Count == 0 ? [] : [new("privacy_consent", "KVKK", "Açık rızası var", CriterionKind.Choice, notices)];
    }

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken)
    {
        if (key != "privacy_consent" || !Guid.TryParse(value, out var noticeId))
        {
            return null;
        }

        var granted = await db.ConsentRecords.Where(c => c.NoticeId == noticeId)
            .GroupBy(c => c.ContactId)
            .Select(g => g.OrderByDescending(c => c.RecordedAt).First())
            .Where(c => c.Granted)
            .Select(c => c.ContactId)
            .ToListAsync(cancellationToken);
        return new(granted);
    }

    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var open = await db.Requests.CountAsync(r => r.Status == RequestStatus.New || r.Status == RequestStatus.InProgress, cancellationToken);
        var overdue = await requests.OverdueCountAsync(cancellationToken);
        return open == 0
            ? []
            : [new("Açık KVKK başvurusu", open.ToString(), overdue > 0 ? $"{overdue} başvurunun süresi geçti" : "30 gün içinde yanıtlanmalı", "privacy/requests", 50, PrivacyPermissions.Read)];
    }
}
