using Arma.Modules.Activities.Application;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Automation.Application;
using Arma.Modules.Automation.Domain;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Sales.Application;
using Arma.Modules.Sales.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class AutomationTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Rules_need_a_trigger_target_and_complete_actions()
    {
        Rule.Create(Input(TriggerKind.TimelineEvent, source: "Comms")).Error!.Code.ShouldBe("automation.source");
        Rule.Create(Input(TriggerKind.SegmentEntered)).Error!.Code.ShouldBe("automation.segment");
        Rule.Create(Input(TriggerKind.TimelineEvent, source: "Sales", actions: [])).Error!.Code.ShouldBe("automation.no_actions");
        Rule.Create(Input(TriggerKind.TimelineEvent, source: "Sales", actions: [new(ActionKind.SendEmail, Subject: "Merhaba")])).Error!.Code.ShouldBe("automation.action");

        var rule = Rule.Create(Input(TriggerKind.TimelineEvent, source: "Sales", subject: "satış kazanıldı")).Value!;
        rule.Matches("Sales", "Satış kazanıldı: Web sitesi").ShouldBeTrue();
        rule.Matches("Sales", "Satış kaybedildi: Web sitesi").ShouldBeFalse();
        rule.Matches("Giving", "Satış kazanıldı").ShouldBeFalse();
    }

    [Fact]
    public async Task Timeline_rule_runs_once_per_source_and_applies_actions()
    {
        RequiresDatabase.Check();
        await Scoped<AutomationService, Guid>(s => s.SaveAsync(null, Input(TriggerKind.TimelineEvent, source: "Sales", subject: "Satış kazanıldı",
            actions: [new(ActionKind.AddTag, TagName: "Kazanılmış müşteri"), new(ActionKind.CreateTask, Subject: "Hoş geldin araması", DueDays: 3)]), ct).Value());
        var customer = await Person("Kural", "Müşteri");
        var pipeline = (await Scoped<SalesService, IReadOnlyList<PipelineView>>(s => s.PipelinesAsync(ct))).Single(p => p.IsDefault);
        var opportunity = await Scoped<SalesService, Guid>(s => s.CreateAsync(new OpportunityInput("Otomasyonlu iş", customer, 100m, null, null, null), cancellationToken: ct).Value());
        var won = pipeline.Stages.Single(s => s.Kind == StageKind.Won).Id;
        await Scoped<SalesService, Guid>(s => s.MoveAsync(opportunity, won, cancellationToken: ct).Value());

        var timeline = await EventuallyAsync(
            () => factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>().TimelineAsync(customer, cancellationToken: ct)),
            t => t.OpenTasks.Any(a => a.Subject == "Hoş geldin araması"));
        timeline.OpenTasks.Single(a => a.Subject == "Hoş geldin araması").Kind.ShouldBe(ActivityKind.Task);
        var tagged = await EventuallyAsync(TaggedAsync, ids => ids.Contains(customer));
        tagged.ShouldContain(customer);

        // Fırsat açılıp yeniden kazanılsa da aynı kaynak için kural tekrar çalışmaz.
        await Scoped<SalesService, Guid>(s => s.MoveAsync(opportunity, pipeline.Stages[0].Id, cancellationToken: ct).Value());
        await Scoped<SalesService, Guid>(s => s.MoveAsync(opportunity, won, cancellationToken: ct).Value());
        await Task.Delay(1500, ct);
        var tasks = await factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>().TimelineAsync(customer, cancellationToken: ct));
        tasks.OpenTasks.Count(a => a.Subject == "Hoş geldin araması").ShouldBe(1);

        async Task<IReadOnlyList<Guid>> TaggedAsync()
        {
            var tags = await Scoped<IContactAudiences, IReadOnlyList<AudienceList>>(a => a.TagsAsync(ct));
            var tag = tags.FirstOrDefault(t => t.Name == "Kazanılmış müşteri");
            return tag is null ? [] : [.. (await Scoped<IContactAudiences, IReadOnlyList<AudienceMember>>(a => a.MembersAsync(null, tag.Id, ct))).Select(m => m.Id)];
        }
    }

    [Fact]
    public async Task Segment_rule_skips_existing_matches_and_handles_newcomers_once()
    {
        RequiresDatabase.Check();
        var group = await Scoped<TaxonomyService, Guid>(t => t.CreateGroupAsync("Segment kuralı grubu", null, ct).Value());
        var segment = await Scoped<ContactSearchService, Guid>(s => s.SaveAsync(null, "Segmentli kişiler", [new SearchCriterion("search", "Segmentli")], ct).Value());
        var early = await Person("Erken", "Segmentli");
        var rule = await Scoped<AutomationService, Guid>(s => s.SaveAsync(null, Input(TriggerKind.SegmentEntered, segment: segment,
            actions: [new(ActionKind.AddToGroup, GroupId: group), new(ActionKind.SendEmail, Subject: "Hoş geldin {{ad}}", Body: "Merhaba {{adsoyad}}")]), ct).Value());

        (await Scoped<AutomationService, int>(s => s.RunSegmentAsync(rule, ct).Value())).ShouldBe(0);
        var late = await Person("Geç", "Segmentli");
        (await Scoped<AutomationService, int>(s => s.RunSegmentAsync(rule, ct).Value())).ShouldBe(1);
        (await Scoped<AutomationService, int>(s => s.RunSegmentAsync(rule, ct).Value())).ShouldBe(0);

        var runs = await Scoped<AutomationService, IReadOnlyList<RunView>>(s => s.RunsAsync(rule, 100, ct));
        runs.Single(r => r.ContactId == early).Status.ShouldBe(RunStatus.Skipped);
        runs.Single(r => r.ContactId == late).Status.ShouldBe(RunStatus.Done);
        var members = await EventuallyAsync(
            () => Scoped<IContactAudiences, IReadOnlyList<AudienceMember>>(a => a.MembersAsync(group, null, ct)),
            m => m.Count > 0);
        members.Select(m => m.Id).ShouldBe([late]);
        (await Scoped<AutomationService, RuleView?>(s => s.GetAsync(rule, ct)))!.RunCount.ShouldBe(1);
    }

    private static RuleInput Input(TriggerKind trigger, string? source = null, string? subject = null, Guid? segment = null, IReadOnlyList<RuleAction>? actions = null) =>
        new("Test kuralı " + Guid.NewGuid().ToString("N")[..6], true, trigger, segment, source, subject, false,
            actions ?? [new RuleAction(ActionKind.AddTag, TagName: "Otomasyon")]);

    private Task<Guid> Person(string first, string last) =>
        Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest(first, last, $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.org", null, null), ct).Value());

    private async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var value = await read();
            if (done(value) || DateTime.UtcNow > deadline)
            {
                return value;
            }

            await Task.Delay(250, ct);
        }
    }

    private Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull =>
        factory.WithScopeAsync(sp => action(sp.GetRequiredService<TService>()));
}
