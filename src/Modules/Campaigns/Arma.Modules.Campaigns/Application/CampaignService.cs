using Arma.Infrastructure.Messaging;
using Arma.Modules.Campaigns.Domain;
using Arma.Modules.Campaigns.Infrastructure;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Identity.Contracts;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Campaigns.Application;

public sealed record CampaignSummary(
    Guid Id, string Name, CampaignStatus Status, DateOnly? StartOn, DateOnly? EndOn, decimal? GoalAmount, decimal Raised, int LinkCount, string? OwnerName);

public sealed record CampaignView(
    Guid Id, string Name, string? Description, CampaignStatus Status, DateOnly? StartOn, DateOnly? EndOn, decimal? GoalAmount, Guid? OwnerUserId, string? OwnerName);

public sealed record LinkResult(Guid LinkId, string Kind, string KindLabel, Guid ItemId, string ItemName, string? Href, IReadOnlyList<CampaignMetric> Metrics);

public sealed record CampaignResults(decimal Raised, int ReachedContacts, IReadOnlyList<LinkResult> Links);

public sealed record SourceOption(string Kind, string Label, string? Permission, IReadOnlyList<CampaignItem> Items);

public static class CampaignErrors
{
    public static readonly Error NotFound = new("campaigns.not_found", "Kampanya bulunamadı.");
    public static readonly Error UnknownSource = new("campaigns.source", "Bu tür kayıt kampanyaya bağlanamıyor.");
    public static readonly Error AlreadyLinked = new("campaigns.linked", "Bu kayıt kampanyaya zaten bağlı.");
}

/// <summary>Kampanyalar ve sonuçları. Sayılar bağlı kaydın modülünden <see cref="ICampaignSource"/> ile alınır.</summary>
public sealed class CampaignService(
    CampaignsDbContext db,
    IEnumerable<ICampaignSource> sources,
    IUserDirectory users,
    IContactAudiences audiences,
    IDbContextOutbox outbox,
    TimeProvider clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IReadOnlyList<CampaignSummary>> ListAsync(CampaignStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = db.Campaigns.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(c => c.Status == s);
        }

        var campaigns = await query.OrderBy(c => c.Status).ThenByDescending(c => c.StartOn).ThenBy(c => c.Name).ToListAsync(cancellationToken);
        var owners = await OwnersAsync(campaigns, cancellationToken);
        var list = new List<CampaignSummary>();
        foreach (var c in campaigns)
        {
            var raised = c.GoalAmount is null ? 0 : await RaisedAsync(c, cancellationToken);
            list.Add(new(c.Id, c.Name, c.Status, c.StartOn, c.EndOn, c.GoalAmount, raised, c.Links.Count, c.OwnerUserId is { } o && owners.TryGetValue(o, out var n) ? n : null));
        }

        return list;
    }

    public Task<IReadOnlyList<AudienceList>> GroupsAsync(CancellationToken cancellationToken = default) => audiences.GroupsAsync(cancellationToken);

    public Task<IReadOnlyList<UserSummary>> UsersAsync(CancellationToken cancellationToken = default) => users.ActiveUsersAsync(cancellationToken);

    public async Task<CampaignView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var c = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (c is null)
        {
            return null;
        }

        var owners = await OwnersAsync([c], cancellationToken);
        return new(c.Id, c.Name, c.Description, c.Status, c.StartOn, c.EndOn, c.GoalAmount, c.OwnerUserId,
            c.OwnerUserId is { } o && owners.TryGetValue(o, out var n) ? n : null);
    }

    public async Task<Result<Guid>> SaveAsync(Guid? id, CampaignInput input, CancellationToken cancellationToken = default)
    {
        Campaign campaign;
        if (id is null)
        {
            var created = Campaign.Create(input);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            campaign = created.Value!;
            db.Campaigns.Add(campaign);
        }
        else
        {
            var existing = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
            if (existing is null)
            {
                return CampaignErrors.NotFound;
            }

            var updated = existing.Update(input);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }

            campaign = existing;
        }

        await db.SaveChangesAsync(cancellationToken);
        return campaign.Id;
    }

    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        db.Campaigns.Remove(campaign);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>Bağlanabilir kayıt türleri ve kayıtları. <paramref name="canSee"/> kullanıcının o modülü görme yetkisini denetler.</summary>
    public async Task<IReadOnlyList<SourceOption>> SourceOptionsAsync(Func<string, bool> canSee, CancellationToken cancellationToken = default)
    {
        var list = new List<SourceOption>();
        foreach (var source in sources.Where(s => s.Permission is null || canSee(s.Permission)).OrderBy(s => s.Order))
        {
            list.Add(new(source.Kind, source.Label, source.Permission, await source.ItemsAsync(cancellationToken)));
        }

        return list;
    }

    public async Task<Result<Guid>> LinkAsync(Guid id, string kind, Guid itemId, CancellationToken cancellationToken = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        var source = sources.FirstOrDefault(s => s.Kind == kind);
        if (source is null || (await source.ItemsAsync(cancellationToken)).All(i => i.Id != itemId))
        {
            return CampaignErrors.UnknownSource;
        }

        if (!campaign.Link(kind, itemId))
        {
            return CampaignErrors.AlreadyLinked;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> UnlinkAsync(Guid id, Guid linkId, CancellationToken cancellationToken = default)
    {
        var campaign = await db.Campaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        campaign.Unlink(linkId);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>Bağlı kayıtların sonuçları, hedefe sayılan toplam ve ulaşılan (tekil) kişi sayısı.</summary>
    public async Task<CampaignResults?> ResultsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var campaign = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        var period = new CampaignPeriod(campaign.StartOn, campaign.EndOn);
        var results = new List<LinkResult>();
        var reached = new HashSet<Guid>();
        foreach (var group in campaign.Links.GroupBy(l => l.Kind))
        {
            var source = sources.FirstOrDefault(s => s.Kind == group.Key);
            if (source is null)
            {
                continue;
            }

            var items = (await source.ItemsAsync(cancellationToken)).ToDictionary(i => i.Id, i => i.Name);
            foreach (var link in group)
            {
                var metrics = await source.MetricsAsync([link.ItemId], period, cancellationToken);
                results.Add(new(link.Id, link.Kind, source.Label, link.ItemId, items.GetValueOrDefault(link.ItemId, "Silinmiş kayıt"),
                    items.ContainsKey(link.ItemId) ? source.Href(link.ItemId) : null, metrics));
            }

            reached.UnionWith(await source.ContactIdsAsync([.. group.Select(l => l.ItemId)], period, cancellationToken));
        }

        var raised = results.SelectMany(r => r.Metrics).Where(m => m.CountsTowardGoal).Sum(m => m.Value);
        return new(raised, reached.Count, results);
    }

    /// <summary>Kampanyanın ulaştığı herkesi bir gruba ekler; teşekkür iletisi veya sonraki kampanya için hedef kitle olur.</summary>
    public async Task<Result<int>> AddReachedToGroupAsync(Guid id, Guid groupId, CancellationToken cancellationToken = default)
    {
        var campaign = await db.Campaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        var period = new CampaignPeriod(campaign.StartOn, campaign.EndOn);
        var reached = new HashSet<Guid>();
        foreach (var group in campaign.Links.GroupBy(l => l.Kind))
        {
            if (sources.FirstOrDefault(s => s.Kind == group.Key) is { } source)
            {
                reached.UnionWith(await source.ContactIdsAsync([.. group.Select(l => l.ItemId)], period, cancellationToken));
            }
        }

        outbox.Enroll(db);
        foreach (var contactId in reached)
        {
            await outbox.PublishAsync(new LabelContact(contactId, null, groupId));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return reached.Count;
    }

    internal async Task<int> ActiveCountAsync(CancellationToken cancellationToken) =>
        await db.Campaigns.CountAsync(c => c.Status == CampaignStatus.Active, cancellationToken);

    private async Task<decimal> RaisedAsync(Campaign campaign, CancellationToken cancellationToken)
    {
        var period = new CampaignPeriod(campaign.StartOn, campaign.EndOn);
        var total = 0m;
        foreach (var group in campaign.Links.GroupBy(l => l.Kind))
        {
            if (sources.FirstOrDefault(s => s.Kind == group.Key) is { } source)
            {
                total += (await source.MetricsAsync([.. group.Select(l => l.ItemId)], period, cancellationToken)).Where(m => m.CountsTowardGoal).Sum(m => m.Value);
            }
        }

        return total;
    }

    private async Task<IReadOnlyDictionary<Guid, string>> OwnersAsync(IEnumerable<Campaign> campaigns, CancellationToken cancellationToken)
    {
        var ids = campaigns.Where(c => c.OwnerUserId is not null).Select(c => c.OwnerUserId!.Value).Distinct().ToList();
        return ids.Count == 0 ? new Dictionary<Guid, string>() : (await users.FindManyAsync(ids, cancellationToken)).ToDictionary(p => p.Key, p => p.Value.FullName);
    }
}
