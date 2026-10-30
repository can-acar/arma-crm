using Arma.Infrastructure.Messaging;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Automation.Domain;
using Arma.Modules.Automation.Infrastructure;
using Arma.Modules.Comms.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Automation.Application;

public sealed record RuleView(
    Guid Id, string Name, bool IsActive, TriggerKind Trigger, Guid? SegmentId, string? SegmentName, string? SourceModule, string? SubjectContains,
    bool IncludeExisting, IReadOnlyList<RuleAction> Actions, DateTimeOffset? LastRunAt, int RunCount);

public sealed record RunView(Guid ContactId, string ContactName, RunStatus Status, DateTimeOffset RanAt, string Key);

public static class AutomationErrors
{
    public static readonly Error NotFound = new("automation.not_found", "Kural bulunamadı.");
    public static readonly Error SegmentNotFound = new("automation.segment_missing", "Kuralın kayıtlı araması silinmiş.");
}

/// <summary>Otomasyon kurallarının yönetimi ve çalıştırılması. İşlemler outbox ile ilgili modüle gider.</summary>
public sealed class AutomationService(
    AutomationDbContext db,
    IContactSegments segments,
    IContactAudiences audiences,
    IContactDirectory contacts,
    IDbContextOutbox outbox,
    TimeProvider clock,
    ILogger<AutomationService> logger)
{
    /// <summary>Bir çalıştırmada en fazla bu kadar kişi işlenir; kalanlar sonraki çalıştırmaya kalır.</summary>
    internal const int BatchSize = 500;

    public async Task<IReadOnlyList<RuleView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rules = await db.Rules.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return await ToViewsAsync(rules, cancellationToken);
    }

    public async Task<RuleView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await db.Rules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return rule is null ? null : (await ToViewsAsync([rule], cancellationToken))[0];
    }

    public Task<IReadOnlyList<SegmentInfo>> SegmentsAsync(CancellationToken cancellationToken = default) => segments.SegmentsAsync(cancellationToken);

    public Task<IReadOnlyList<AudienceList>> GroupsAsync(CancellationToken cancellationToken = default) => audiences.GroupsAsync(cancellationToken);

    public async Task<Result<Guid>> SaveAsync(Guid? id, RuleInput input, CancellationToken cancellationToken = default)
    {
        Rule rule;
        if (id is null)
        {
            var created = Rule.Create(input);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            rule = created.Value!;
            db.Rules.Add(rule);
        }
        else
        {
            var existing = await db.Rules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (existing is null)
            {
                return AutomationErrors.NotFound;
            }

            var updated = existing.Update(input);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }

            rule = existing;
        }

        await db.SaveChangesAsync(cancellationToken);
        return rule.Id;
    }

    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await db.Rules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule is null)
        {
            return AutomationErrors.NotFound;
        }

        db.Rules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<IReadOnlyList<RunView>> RunsAsync(Guid ruleId, int take = 100, CancellationToken cancellationToken = default)
    {
        var runs = await db.Runs.AsNoTracking().Where(r => r.RuleId == ruleId).OrderByDescending(r => r.RanAt).Take(take).ToListAsync(cancellationToken);
        var names = await contacts.FindManyAsync(runs.Select(r => r.ContactId).Distinct().ToList(), cancellationToken);
        return [.. runs.Select(r => new RunView(r.ContactId, names.TryGetValue(r.ContactId, out var c) ? c.DisplayName : "Silinmiş kişi", r.Status, r.RanAt, r.Key))];
    }

    /// <summary>Açık segment kurallarını denetler; segmente yeni giren kişilere işlemleri uygular. İşlenen kişi sayısını döner.</summary>
    public async Task<int> RunSegmentsAsync(CancellationToken cancellationToken = default)
    {
        var total = 0;
        var ids = await db.Rules.Where(r => r.IsActive && r.Trigger == TriggerKind.SegmentEntered).Select(r => r.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            var result = await RunSegmentAsync(id, cancellationToken);
            total += result.IsSuccess ? result.Value : 0;
        }

        return total;
    }

    public async Task<Result<int>> RunSegmentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rule = await db.Rules.FirstOrDefaultAsync(r => r.Id == id && r.Trigger == TriggerKind.SegmentEntered, cancellationToken);
        if (rule is null)
        {
            return AutomationErrors.NotFound;
        }

        if (!rule.IsActive)
        {
            return 0;
        }

        var matches = await segments.MatchAsync(rule.SegmentId!.Value, cancellationToken);
        if (matches is null)
        {
            return AutomationErrors.SegmentNotFound;
        }

        var seen = (await db.Runs.Where(r => r.RuleId == id && r.Key == RuleRun.SegmentKey).Select(r => r.ContactId).ToListAsync(cancellationToken)).ToHashSet();
        var now = clock.GetUtcNow();
        var fresh = matches.Where(c => !seen.Contains(c)).ToList();
        if (rule.BaselinePending)
        {
            db.Runs.AddRange(fresh.Select(c => RuleRun.Create(id, c, RuleRun.SegmentKey, RunStatus.Skipped, now)));
            rule.BaselineDone();
            await db.SaveChangesAsync(cancellationToken);
            return 0;
        }

        var batch = fresh.Take(BatchSize).ToList();
        if (batch.Count == 0)
        {
            return 0;
        }

        await ApplyAsync(rule, batch, RuleRun.SegmentKey, now, cancellationToken);
        return batch.Count;
    }

    /// <summary>Zaman akışına düşen kayda göre kuralları çalıştırır. Aynı kaynak tekrar gelirse (kayıt güncellendi) yeniden çalışmaz.</summary>
    internal async Task OnTimelineAsync(RecordTimelineEntry entry, CancellationToken cancellationToken)
    {
        if (!Rule.Sources.Contains(entry.SourceModule) || entry.ContactIds.Count == 0)
        {
            return;
        }

        var rules = (await db.Rules.Where(r => r.IsActive && r.Trigger == TriggerKind.TimelineEvent && r.SourceModule == entry.SourceModule).ToListAsync(cancellationToken))
            .Where(r => r.Matches(entry.SourceModule, entry.Subject))
            .ToList();
        var key = $"{entry.SourceModule}:{entry.SourceId}";
        foreach (var rule in rules)
        {
            var done = await db.Runs.Where(r => r.RuleId == rule.Id && r.Key == key).Select(r => r.ContactId).ToListAsync(cancellationToken);
            var pending = entry.ContactIds.Distinct().Except(done).ToList();
            if (pending.Count > 0)
            {
                await ApplyAsync(rule, pending, key, clock.GetUtcNow(), cancellationToken);
            }
        }
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        var runs = await db.Runs.Where(r => r.ContactId == from).ToListAsync(cancellationToken);
        var existing = (await db.Runs.Where(r => r.ContactId == to).Select(r => new { r.RuleId, r.Key }).ToListAsync(cancellationToken))
            .Select(r => (r.RuleId, r.Key)).ToHashSet();
        foreach (var run in runs)
        {
            if (existing.Contains((run.RuleId, run.Key)))
            {
                db.Runs.Remove(run);
            }
            else
            {
                run.ChangeContact(to);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyAsync(Rule rule, IReadOnlyList<Guid> contactIds, string key, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var actions = rule.Actions;
        var people = actions.Any(a => a.Kind == ActionKind.SendEmail)
            ? await audiences.FindManyAsync(contactIds, cancellationToken)
            : new Dictionary<Guid, AudienceMember>();
        outbox.Enroll(db);
        foreach (var contactId in contactIds)
        {
            foreach (var action in actions)
            {
                object? message = action.Kind switch
                {
                    ActionKind.AddTag => new LabelContact(contactId, action.TagName, null),
                    ActionKind.AddToGroup => new LabelContact(contactId, null, action.GroupId),
                    ActionKind.CreateTask => new CreateFollowUpTask([contactId], action.Subject!, action.Body, now.AddDays(action.DueDays ?? 0), action.AssigneeUserId),
                    ActionKind.SendEmail when people.TryGetValue(contactId, out var person) && person.Email is not null =>
                        new SendTransactionalEmail(contactId, Merge(action.Subject!, person), Merge(action.Body!, person)),
                    _ => null,
                };
                if (message is not null)
                {
                    await outbox.PublishAsync(message);
                }
            }

            db.Runs.Add(RuleRun.Create(rule.Id, contactId, key, RunStatus.Done, now));
        }

        rule.Ran(now);
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        logger.LogInformation("Otomasyon kuralı {Rule} {Count} kişi için çalıştı", rule.Name, contactIds.Count);
    }

    /// <summary>İleti şablonlarıyla aynı alanlar: {{ad}}, {{soyad}}, {{adsoyad}}, {{eposta}}.</summary>
    internal static string Merge(string text, AudienceMember contact) =>
        text.Replace("{{ad}}", contact.FirstName ?? contact.DisplayName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{soyad}}", contact.LastName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{{adsoyad}}", contact.DisplayName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{eposta}}", contact.Email ?? "", StringComparison.OrdinalIgnoreCase);

    private async Task<IReadOnlyList<RuleView>> ToViewsAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken)
    {
        var names = rules.Any(r => r.SegmentId is not null) ? (await segments.SegmentsAsync(cancellationToken)).ToDictionary(s => s.Id, s => s.Name) : [];
        var ids = rules.Select(r => r.Id).ToList();
        var counts = await db.Runs.Where(r => ids.Contains(r.RuleId) && r.Status == RunStatus.Done).GroupBy(r => r.RuleId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
        return [.. rules.Select(r => new RuleView(
            r.Id, r.Name, r.IsActive, r.Trigger, r.SegmentId, r.SegmentId is { } s && names.TryGetValue(s, out var n) ? n : null,
            r.SourceModule, r.SubjectContains, r.IncludeExisting, r.Actions, r.LastRunAt, counts.GetValueOrDefault(r.Id)))];
    }
}
