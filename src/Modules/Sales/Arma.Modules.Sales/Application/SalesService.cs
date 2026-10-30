using System.Globalization;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Identity.Contracts;
using Arma.Modules.Sales.Domain;
using Arma.Modules.Sales.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Sales.Application;

public sealed record StageView(Guid Id, string Name, int Position, int Probability, StageKind Kind);

public sealed record PipelineView(Guid Id, string Name, bool IsDefault, IReadOnlyList<StageView> Stages);

public sealed record OpportunityView(
    Guid Id, string Title, Guid ContactId, string ContactName, Guid PipelineId, Guid StageId, string StageName, StageKind StageKind, int Probability,
    decimal Amount, DateOnly? ExpectedCloseOn, Guid? OwnerUserId, string? OwnerName, string? Note, DateOnly? ClosedOn, string? LostReason, DateTimeOffset CreatedAt)
{
    public bool Overdue(DateOnly today) => StageKind == StageKind.Open && ExpectedCloseOn < today;
}

public sealed record BoardColumn(StageView Stage, IReadOnlyList<OpportunityView> Items, decimal Total);

public sealed record Board(PipelineView Pipeline, IReadOnlyList<BoardColumn> Columns, decimal OpenTotal, decimal WeightedTotal);

public sealed record BoardFilter(Guid? OwnerUserId = null, string? Search = null, bool IncludeClosedOlderThan30Days = false);

public static class SalesErrors
{
    public static readonly Error NotFound = new("opportunities.not_found", "Fırsat bulunamadı.");
    public static readonly Error PipelineNotFound = new("pipelines.not_found", "Satış hattı bulunamadı.");
    public static readonly Error StageNotFound = new("stages.not_found", "Aşama bulunamadı.");
    public static readonly Error StageInUse = new("stages.in_use", "Silinen aşamada fırsat var; önce fırsatları başka aşamaya taşıyın.");
    public static readonly Error ContactNotFound = new("opportunities.contact", "Kişi veya kurum bulunamadı.");
}

public sealed class SalesService(SalesDbContext db, IContactDirectory contacts, IUserDirectory users, IDbContextOutbox outbox, TimeProvider clock)
{
    internal const string SourceModule = "Sales";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    /// <summary>Satış hatları; hiç yoksa varsayılan hat (Yeni → Görüşme → Teklif → Pazarlık → Kazanıldı/Kaybedildi) oluşturulur.</summary>
    public async Task<IReadOnlyList<PipelineView>> PipelinesAsync(CancellationToken cancellationToken = default)
    {
        if (!await db.Pipelines.AnyAsync(cancellationToken))
        {
            db.Pipelines.Add(Pipeline.CreateDefault());
            await db.SaveChangesAsync(cancellationToken);
        }

        var pipelines = await db.Pipelines.AsNoTracking().OrderByDescending(p => p.IsDefault).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        return [.. pipelines.Select(ToView)];
    }

    public async Task<Result<Guid>> SavePipelineAsync(Guid? id, string name, IReadOnlyList<StageInput> stages, CancellationToken cancellationToken = default)
    {
        Pipeline pipeline;
        if (id is null)
        {
            var created = Pipeline.Create(name);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            pipeline = created.Value!;
            db.Pipelines.Add(pipeline);
        }
        else
        {
            var found = await db.Pipelines.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
            if (found is null)
            {
                return SalesErrors.PipelineNotFound;
            }

            var renamed = found.Rename(name);
            if (!renamed.IsSuccess)
            {
                return renamed.Error!;
            }

            pipeline = found;
        }

        if (stages.Count > 0)
        {
            var removing = pipeline.Stages.Where(s => stages.All(i => i.Id != s.Id)).Select(s => s.Id).ToList();
            if (removing.Count > 0 && await db.Opportunities.AnyAsync(o => removing.Contains(o.StageId), cancellationToken))
            {
                return SalesErrors.StageInUse;
            }

            var replaced = pipeline.ReplaceStages(stages);
            if (!replaced.IsSuccess)
            {
                return replaced.Error!;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return pipeline.Id;
    }

    public async Task<Board> BoardAsync(Guid? pipelineId, BoardFilter filter, CancellationToken cancellationToken = default)
    {
        var pipelines = await PipelinesAsync(cancellationToken);
        var pipeline = pipelines.FirstOrDefault(p => p.Id == pipelineId) ?? pipelines[0];
        var query = db.Opportunities.AsNoTracking().Where(o => o.PipelineId == pipeline.Id);
        if (filter.OwnerUserId is { } owner)
        {
            query = query.Where(o => o.OwnerUserId == owner);
        }

        if (!filter.IncludeClosedOlderThan30Days)
        {
            var since = Today.AddDays(-30);
            query = query.Where(o => o.ClosedOn == null || o.ClosedOn >= since);
        }

        var items = await ToViewsAsync(await query.OrderBy(o => o.SortKey).ToListAsync(cancellationToken), pipeline, cancellationToken);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            items = [.. items.Where(o => o.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) || o.ContactName.Contains(term, StringComparison.CurrentCultureIgnoreCase))];
        }

        var columns = pipeline.Stages.Select(s =>
        {
            var inStage = items.Where(o => o.StageId == s.Id).ToList();
            return new BoardColumn(s, inStage, inStage.Sum(o => o.Amount));
        }).ToList();
        var open = items.Where(o => o.StageKind == StageKind.Open).ToList();
        return new Board(pipeline, columns, open.Sum(o => o.Amount), open.Sum(o => o.Amount * o.Probability / 100m));
    }

    public async Task<OpportunityView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.Opportunities.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var pipeline = ToView(await db.Pipelines.AsNoTracking().FirstAsync(p => p.Id == item.PipelineId, cancellationToken));
        return (await ToViewsAsync([item], pipeline, cancellationToken))[0];
    }

    public async Task<IReadOnlyList<OpportunityView>> ForContactAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var items = await db.Opportunities.AsNoTracking().Where(o => o.ContactId == contactId).OrderByDescending(o => o.CreatedAt).ToListAsync(cancellationToken);
        var pipelines = (await PipelinesAsync(cancellationToken)).ToDictionary(p => p.Id);
        var views = new List<OpportunityView>();
        foreach (var group in items.GroupBy(o => o.PipelineId))
        {
            views.AddRange(await ToViewsAsync([.. group], pipelines[group.Key], cancellationToken));
        }

        return [.. views.OrderByDescending(v => v.CreatedAt)];
    }

    /// <summary>Yeni fırsat; aşama verilmezse hattın ilk aşamasına, sütunun en altına eklenir.</summary>
    public async Task<Result<Guid>> CreateAsync(OpportunityInput input, Guid? pipelineId = null, Guid? stageId = null, CancellationToken cancellationToken = default)
    {
        if (await contacts.FindAsync(input.ContactId, cancellationToken) is null)
        {
            return SalesErrors.ContactNotFound;
        }

        var pipelines = await PipelinesAsync(cancellationToken);
        var pipeline = pipelines.FirstOrDefault(p => p.Id == pipelineId) ?? pipelines[0];
        var stage = pipeline.Stages.FirstOrDefault(s => s.Id == stageId) ?? pipeline.Stages[0];
        var created = Opportunity.Create(input, pipeline.Id, stage.Id, await NextSortKeyAsync(stage.Id, cancellationToken));
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        var item = created.Value!;
        db.Opportunities.Add(item);
        if (stage.Kind != StageKind.Open)
        {
            item.MoveTo(await db.Stages.FirstAsync(s => s.Id == stage.Id, cancellationToken), item.SortKey, Today, null);
        }

        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(item, $"Satış fırsatı: {item.Title}", stage.Name));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return item.Id;
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, OpportunityInput input, CancellationToken cancellationToken = default)
    {
        var item = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (item is null)
        {
            return SalesErrors.NotFound;
        }

        if (input.ContactId != item.ContactId && await contacts.FindAsync(input.ContactId, cancellationToken) is null)
        {
            return SalesErrors.ContactNotFound;
        }

        var updated = item.Update(input);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        var stage = await db.Stages.AsNoTracking().FirstAsync(s => s.Id == item.StageId, cancellationToken);
        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(item, Subject(item, stage), stage.Name));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return id;
    }

    /// <summary>
    /// Fırsatı bir aşamaya taşır (panoda sürükle-bırak). <paramref name="beforeId"/> verilirse o kartın üstüne,
    /// verilmezse sütunun sonuna yerleşir.
    /// </summary>
    public async Task<Result<Guid>> MoveAsync(Guid id, Guid stageId, Guid? beforeId = null, string? lostReason = null, CancellationToken cancellationToken = default)
    {
        var item = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (item is null)
        {
            return SalesErrors.NotFound;
        }

        var stage = await db.Stages.FirstOrDefaultAsync(s => s.Id == stageId && s.PipelineId == item.PipelineId, cancellationToken);
        if (stage is null)
        {
            return SalesErrors.StageNotFound;
        }

        double sortKey;
        if (beforeId is { } before && before != id && await db.Opportunities.AsNoTracking().FirstOrDefaultAsync(o => o.Id == before && o.StageId == stageId, cancellationToken) is { } next)
        {
            var previous = await db.Opportunities.AsNoTracking().Where(o => o.StageId == stageId && o.SortKey < next.SortKey && o.Id != id)
                .OrderByDescending(o => o.SortKey).Select(o => (double?)o.SortKey).FirstOrDefaultAsync(cancellationToken);
            sortKey = previous is { } p ? (p + next.SortKey) / 2 : next.SortKey - 1;
        }
        else
        {
            sortKey = item.StageId == stageId && beforeId is null ? item.SortKey : await NextSortKeyAsync(stageId, cancellationToken);
        }

        var changedStage = item.StageId != stageId;
        item.MoveTo(stage, sortKey, Today, lostReason);
        outbox.Enroll(db);
        if (changedStage)
        {
            await outbox.PublishAsync(Timeline(item, Subject(item, stage), stage.Kind == StageKind.Lost ? item.LostReason : stage.Name));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.Opportunities.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (item is null)
        {
            return SalesErrors.NotFound;
        }

        db.Opportunities.Remove(item);
        outbox.Enroll(db);
        await outbox.PublishAsync(new RemoveTimelineEntry(SourceModule, item.Id.ToString()));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return id;
    }

    /// <summary>Panodaki özet: açık hat, ağırlıklı tahmin, bu ay kazanılan.</summary>
    internal async Task<(decimal Open, decimal Weighted, decimal WonThisMonth, int WonCount)> SummaryAsync(CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        var rows = await db.Opportunities.AsNoTracking()
            .Join(db.Stages, o => o.StageId, s => s.Id, (o, s) => new { o.Amount, s.Kind, s.Probability, o.ClosedOn })
            .Where(x => x.Kind == StageKind.Open || (x.Kind == StageKind.Won && x.ClosedOn >= monthStart))
            .ToListAsync(cancellationToken);
        var open = rows.Where(r => r.Kind == StageKind.Open).ToList();
        var won = rows.Where(r => r.Kind == StageKind.Won).ToList();
        return (open.Sum(r => r.Amount), open.Sum(r => r.Amount * r.Probability / 100m), won.Sum(r => r.Amount), won.Count);
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        foreach (var item in await db.Opportunities.Where(o => o.ContactId == from).ToListAsync(cancellationToken))
        {
            item.ChangeContact(to);
        }

        foreach (var quote in await db.Quotes.Where(q => q.ContactId == from).ToListAsync(cancellationToken))
        {
            quote.ChangeContact(to);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal static string Money(decimal amount) => string.Format(Turkish, "{0:N2} TL", amount);

    private static string Subject(Opportunity item, Stage stage) => stage.Kind switch
    {
        StageKind.Won => $"Satış kazanıldı: {item.Title} ({Money(item.Amount)})",
        StageKind.Lost => $"Satış kaybedildi: {item.Title}",
        _ => $"Satış fırsatı: {item.Title}",
    };

    private async Task<double> NextSortKeyAsync(Guid stageId, CancellationToken cancellationToken) =>
        (await db.Opportunities.Where(o => o.StageId == stageId).MaxAsync(o => (double?)o.SortKey, cancellationToken) ?? 0) + 1;

    private RecordTimelineEntry Timeline(Opportunity item, string subject, string? body) =>
        new([item.ContactId], ActivityKind.System, subject, body, clock.GetUtcNow(), SourceModule, item.Id.ToString());

    private static PipelineView ToView(Pipeline p) =>
        new(p.Id, p.Name, p.IsDefault, [.. p.Stages.Where(s => !s.IsDeleted).OrderBy(s => s.Position).Select(s => new StageView(s.Id, s.Name, s.Position, s.Probability, s.Kind))]);

    private async Task<List<OpportunityView>> ToViewsAsync(IReadOnlyList<Opportunity> items, PipelineView pipeline, CancellationToken cancellationToken)
    {
        var names = await contacts.FindManyAsync([.. items.Select(o => o.ContactId).Distinct()], cancellationToken);
        var owners = await users.FindManyAsync([.. items.Where(o => o.OwnerUserId is not null).Select(o => o.OwnerUserId!.Value).Distinct()], cancellationToken);
        var stages = pipeline.Stages.ToDictionary(s => s.Id);
        return [.. items.Select(o =>
        {
            var stage = stages.GetValueOrDefault(o.StageId) ?? new StageView(o.StageId, "?", 0, 0, StageKind.Open);
            return new OpportunityView(o.Id, o.Title, o.ContactId, names.TryGetValue(o.ContactId, out var c) ? c.DisplayName : "(silinmiş kişi)", o.PipelineId,
                o.StageId, stage.Name, stage.Kind, stage.Probability, o.Amount, o.ExpectedCloseOn, o.OwnerUserId,
                o.OwnerUserId is { } u && owners.TryGetValue(u, out var owner) ? owner.FullName : null, o.Note, o.ClosedOn, o.LostReason, o.CreatedAt);
        })];
    }
}
