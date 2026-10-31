using Arma.Modules.Activities.Application;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Grants.Application;
using Arma.Modules.Grants.Domain;
using Arma.SharedKernel.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class GrantTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Status_changes_require_an_awarded_amount_and_stamp_dates()
    {
        var today = new DateOnly(2026, 5, 10);
        var grant = Grant.Create(Guid.NewGuid(), "Okuma atölyesi", null, "eur", 1000m, null, new DateOnly(2026, 6, 1), new DateOnly(2026, 5, 1), null, null);
        grant.Error!.Code.ShouldBe("grants.period");

        var item = Grant.Create(Guid.NewGuid(), "Okuma atölyesi", null, "eur", 1000m, null, null, null, null, null).Value!;
        item.Currency.ShouldBe("EUR");
        item.ChangeStatus(GrantStatus.Closed, null, today).Error!.Code.ShouldBe("grants.not_awarded");
        item.ChangeStatus(GrantStatus.Awarded, null, today).Error!.Code.ShouldBe("grants.awarded");
        item.ChangeStatus(GrantStatus.Awarded, 800m, today).IsSuccess.ShouldBeTrue();
        (item.SubmittedOn, item.DecidedOn, item.Awarded).ShouldBe((today, today, 800m));
        item.ChangeStatus(GrantStatus.Closed, null, today.AddMonths(6)).IsSuccess.ShouldBeTrue();
        (item.DecidedOn, item.Awarded).ShouldBe((today, 800m));
        item.ChangeStatus(GrantStatus.Preparing, null, today).IsSuccess.ShouldBeTrue();
        (item.SubmittedOn, item.DecidedOn, item.Awarded).ShouldBe(((DateOnly?)null, (DateOnly?)null, (decimal?)null));

        GrantMilestone.Create(item.Id, MilestoneKind.Payment, "1. dilim", today, null).Error!.Code.ShouldBe("grant_milestones.amount");
        GrantMilestone.Create(item.Id, MilestoneKind.Report, "Ara rapor", today, 50m).Value!.Amount.ShouldBeNull();
    }

    [Fact]
    public async Task Grant_lifecycle_tracks_milestones_payments_timeline_and_merge()
    {
        RequiresDatabase.Check();
        var funder = await Scoped<ContactService, Guid>(s => s.CreateOrganizationAsync(new CreateOrganizationRequest("Kalkınma Vakfı " + Guid.NewGuid().ToString("N")[..6], null, null, null), ct).Value());
        var today = DateOnly.FromDateTime(DateTime.Today);
        var id = await Scoped<GrantService, Guid>(s => s.CreateAsync(new SaveGrantRequest(funder, "Kırsal okuma atölyeleri", "Eğitim 2026", null, 250_000m, today.AddDays(10), null, null, null, "Üç köyde atölye"), ct).Value());

        var item = (await Scoped<GrantService, GrantDetail?>(s => s.GetAsync(id, ct)))!.Summary;
        (item.Status, item.NextDueOn, item.NextDueTitle, item.HasOverdue).ShouldBe((GrantStatus.Preparing, (DateOnly?)today.AddDays(10), "Başvuru son günü", false));

        await Scoped<GrantService, Guid>(s => s.ChangeStatusAsync(id, GrantStatus.Submitted, null, ct).Value());
        await Scoped<GrantService, Guid>(s => s.ChangeStatusAsync(id, GrantStatus.Awarded, 200_000m, ct).Value());
        var report = await Scoped<GrantService, Guid>(s => s.AddMilestoneAsync(id, new SaveMilestoneRequest(MilestoneKind.Report, "Ara rapor", today.AddDays(-1), null), ct).Value());
        var first = await Scoped<GrantService, Guid>(s => s.AddMilestoneAsync(id, new SaveMilestoneRequest(MilestoneKind.Payment, "1. dilim", today, 120_000m), ct).Value());
        await Scoped<GrantService, Guid>(s => s.AddMilestoneAsync(id, new SaveMilestoneRequest(MilestoneKind.Payment, "2. dilim", today.AddMonths(6), 80_000m), ct).Value());

        var detail = (await Scoped<GrantService, GrantDetail?>(s => s.GetAsync(id, ct)))!;
        detail.Summary.HasOverdue.ShouldBeTrue();
        detail.Milestones.Single(m => m.Id == report).IsOverdue.ShouldBeTrue();
        (await Scoped<GrantService, GrantSummary>(s => s.SummaryAsync(ct))).Overdue.ShouldBeGreaterThanOrEqualTo(1);

        await Scoped<GrantService, Guid>(s => s.SetMilestoneDoneAsync(first, true, ct).Value());
        await Scoped<GrantService, Guid>(s => s.SetMilestoneDoneAsync(report, true, ct).Value());
        detail = (await Scoped<GrantService, GrantDetail?>(s => s.GetAsync(id, ct)))!;
        (detail.Summary.Received, detail.Summary.HasOverdue, detail.Summary.NextDueTitle).ShouldBe((120_000m, false, "2. dilim"));

        var timeline = await EventuallyAsync(
            () => factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>().TimelineAsync(funder, cancellationToken: ct)),
            t => t.History.Any(a => a.Subject.StartsWith("Hibe ödemesi alındı")));
        timeline.History.ShouldContain(a => a.Subject.StartsWith("Hibe kabul edildi"));
        timeline.History.ShouldContain(a => a.Subject == "Hibe başvurusu: Kırsal okuma atölyeleri");

        var criteria = factory.Services.CreateScope().ServiceProvider.GetServices<IContactCriteriaProvider>().OfType<GrantsInsights>().Single();
        (await criteria.MatchAsync("grant_awarded", null, ct))!.ContactIds.ShouldContain(funder);

        var duplicate = await Scoped<ContactService, Guid>(s => s.CreateOrganizationAsync(new CreateOrganizationRequest("Kalkınma Vakfı (eski)", null, null, null), ct).Value());
        var other = await Scoped<GrantService, Guid>(s => s.CreateAsync(new SaveGrantRequest(duplicate, "Eski başvuru", null, null, 0m, null, null, null, null, null), ct).Value());
        await Scoped<ContactService, Guid>(s => s.MergeAsync(funder, duplicate, ct).Value());
        var moved = await EventuallyAsync(() => Scoped<GrantService, IReadOnlyList<GrantListItem>>(s => s.ListAsync(new GrantFilter(FunderId: funder), ct)), l => l.Count == 2);
        moved.Select(g => g.Id).ShouldBe([id, other], ignoreOrder: true);

        (await Scoped<GrantService, Result<Guid>>(s => s.CreateAsync(new SaveGrantRequest(Guid.NewGuid(), "Yok", null, null, 0m, null, null, null, null, null), ct))).Error.ShouldBe(GrantErrors.FunderNotFound);
    }

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
