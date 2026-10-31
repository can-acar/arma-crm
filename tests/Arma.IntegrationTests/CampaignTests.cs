using Arma.Modules.Campaigns.Application;
using Arma.Modules.Campaigns.Domain;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Events.Application;
using Arma.Modules.Events.Domain;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class CampaignTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Campaigns_need_a_name_a_valid_period_and_a_positive_goal()
    {
        var today = new DateOnly(2026, 11, 1);
        Campaign.Create(Input("", today, null, null)).Error!.Code.ShouldBe("campaigns.name");
        Campaign.Create(Input("Kış", today, today.AddDays(-1), null)).Error!.Code.ShouldBe("campaigns.period");
        Campaign.Create(Input("Kış", today, null, 0m)).Error!.Code.ShouldBe("campaigns.goal");

        var campaign = Campaign.Create(Input("Kış", today, null, 1000m)).Value!;
        var fund = Guid.NewGuid();
        campaign.Link("fund", fund).ShouldBeTrue();
        campaign.Link("fund", fund).ShouldBeFalse();
        campaign.Unlink(campaign.Links[0].Id);
        campaign.Links.ShouldBeEmpty();
    }

    [Fact]
    public async Task Results_sum_linked_records_in_the_period_and_count_each_contact_once()
    {
        RequiresDatabase.Check();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var fund = await Scoped<FundService, Guid>(s => s.CreateAsync(new SaveFundRequest("Kampanya fonu " + Suffix(), null, true, false), ct).Value());
        var donor = await Person("Kampanya", "Bağışçı");
        var guest = await Person("Kampanya", "Konuk");
        await Donate(donor, 400m, today, fund);
        await Donate(guest, 100m, today, fund);
        await Donate(donor, 999m, today.AddDays(-30), fund);

        var starts = DateTimeOffset.Now.AddDays(7);
        var evt = await Scoped<EventService, Guid>(s => s.SaveAsync(null, new EventDetails("Yardım gecesi", "yardim-" + Suffix(), null, null, starts, starts.AddHours(3), null, null, null), ct).Value());
        await Scoped<EventService, Guid>(s => s.RegisterAsync(evt, guest, null, "staff", null, ct).Value());

        var campaign = await Scoped<CampaignService, Guid>(s => s.SaveAsync(null, Input("Kış yardımı " + Suffix(), today.AddDays(-7), null, 1000m), ct).Value());
        await Scoped<CampaignService, Guid>(s => s.LinkAsync(campaign, "fund", fund, ct).Value());
        await Scoped<CampaignService, Guid>(s => s.LinkAsync(campaign, "event", evt, ct).Value());
        (await factory.WithScopeAsync(sp => sp.GetRequiredService<CampaignService>().LinkAsync(campaign, "fund", fund, ct))).Error!.Code.ShouldBe("campaigns.linked");
        (await factory.WithScopeAsync(sp => sp.GetRequiredService<CampaignService>().LinkAsync(campaign, "fund", Guid.NewGuid(), ct))).Error!.Code.ShouldBe("campaigns.source");

        var results = (await Scoped<CampaignService, CampaignResults?>(s => s.ResultsAsync(campaign, ct)))!;
        results.Raised.ShouldBe(500m);
        results.ReachedContacts.ShouldBe(2);
        results.Links.Count.ShouldBe(2);
        results.Links.Single(l => l.Kind == "event").Metrics.Single(m => m.Label == "Etkinlik kaydı").Value.ShouldBe(1);

        var summary = (await Scoped<CampaignService, IReadOnlyList<CampaignSummary>>(s => s.ListAsync(cancellationToken: ct))).Single(c => c.Id == campaign);
        (summary.Raised, summary.LinkCount).ShouldBe((500m, 2));

        var group = await Scoped<TaxonomyService, Guid>(t => t.CreateGroupAsync("Kampanya destekçileri " + Suffix(), null, ct).Value());
        (await Scoped<CampaignService, int>(s => s.AddReachedToGroupAsync(campaign, group, ct).Value())).ShouldBe(2);
        var members = await EventuallyAsync(
            () => Scoped<IContactAudiences, IReadOnlyList<AudienceMember>>(a => a.MembersAsync(group, null, ct)),
            m => m.Count >= 2);
        members.Select(m => m.Id).ShouldBe([donor, guest], ignoreOrder: true);

        var link = results.Links.Single(l => l.Kind == "fund").LinkId;
        await Scoped<CampaignService, Guid>(s => s.UnlinkAsync(campaign, link, ct).Value());
        (await Scoped<CampaignService, CampaignResults?>(s => s.ResultsAsync(campaign, ct)))!.Raised.ShouldBe(0m);
        await Scoped<CampaignService, Guid>(s => s.LinkAsync(campaign, "fund", fund, ct).Value());
        (await Scoped<CampaignService, CampaignResults?>(s => s.ResultsAsync(campaign, ct)))!.Raised.ShouldBe(500m);
    }

    private static CampaignInput Input(string name, DateOnly start, DateOnly? end, decimal? goal) =>
        new(name, null, CampaignStatus.Active, start, end, goal, null);

    private static string Suffix() => Guid.NewGuid().ToString("N")[..6];

    private Task<Guid> Donate(Guid contact, decimal amount, DateOnly on, Guid fund) =>
        Scoped<DonationService, Guid>(s => s.RecordAsync(new SaveDonationRequest(contact, amount, null, on, fund, PaymentMethod.Cash, null, null, null), ct).Value());

    private Task<Guid> Person(string first, string last)
    {
        var suffix = Suffix();
        return Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest(first, last + suffix, $"{first}.{suffix}@example.org".ToLowerInvariant(), null, null), ct).Value());
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
