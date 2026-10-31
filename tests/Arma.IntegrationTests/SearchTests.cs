using Arma.Modules.Comms.Application;
using Arma.Modules.Comms.Domain;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Domain;
using Arma.SharedKernel.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class SearchTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Criteria_from_several_modules_are_combined()
    {
        RequiresDatabase.Check();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var loyal = await Person("Sadık", "sadik@example.org");
        var lapsed = await Person("Kayıp", "kayip@example.org");
        var never = await Person("Hiç", null);
        await Gift(loyal, 1000m, today.AddYears(-1));
        await Gift(loyal, 2500m, today);
        await Gift(lapsed, 300m, today.AddYears(-1));
        await Scoped<ConsentService, Guid>(s => s.SetAsync(loyal, Channel.Email, ConsentStatus.Approved, "form", ct).Value());
        await Scoped<ConsentService, Guid>(s => s.SetAsync(lapsed, Channel.Email, ConsentStatus.Approved, "form", ct).Value());
        var since = today.AddMonths(-6).ToString("yyyy-MM-dd");

        var lapsedWithConsent = await Find(new("gift_lapsed_since", since), new("consent_email", null));
        lapsedWithConsent.ShouldBe([lapsed]);

        var big = await Find(new("gift_total_gte", "3000"), new("has_email", null));
        big.ShouldBe([loyal]);

        var nonDonors = await Find(new("gift_never", null), new("search", "Hiç"));
        nonDonors.ShouldBe([never]);

        var invalid = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactSearchService>().SearchAsync([new("gift_since", "dün")], 1, 10, ct));
        invalid.Error!.Code.ShouldBe("search.invalid_value");

        var group = await Scoped<TaxonomyService, Guid>(s => s.CreateGroupAsync("Geri kazanılacaklar", null, ct).Value());
        (await Scoped<ContactSearchService, int>(s => s.AddToGroupAsync([new("gift_lapsed_since", since)], group, ct).Value())).ShouldBeGreaterThanOrEqualTo(1);

        var saved = await Scoped<ContactSearchService, Guid>(s => s.SaveAsync(null, "Kayıp bağışçılar", [new("gift_lapsed_since", since)], ct).Value());
        var list = await Scoped<ContactSearchService, IReadOnlyList<SavedSearchView>>(s => s.SavedAsync(ct));
        list.Single(s => s.Id == saved).Criteria.ShouldHaveSingleItem().Value.ShouldBe(since);
    }

    [Fact]
    public async Task Dashboard_collects_tiles_from_modules()
    {
        RequiresDatabase.Check();
        var tiles = await factory.WithScopeAsync(async sp =>
        {
            var all = new List<DashboardTile>();
            foreach (var contributor in sp.GetServices<IDashboardContributor>())
            {
                all.AddRange(await contributor.TilesAsync(ct));
            }

            return all;
        });
        tiles.ShouldContain(t => t.Title == "Kişi ve kurum");
        tiles.ShouldContain(t => t.Title.EndsWith("bağışları"));
    }

    private async Task<IReadOnlyList<Guid>> Find(params SearchCriterion[] criteria)
    {
        var page = await Scoped<ContactSearchService, PagedResult<ContactListItem>>(s => s.SearchAsync(criteria, 1, 100, ct).Value());
        return page.Items.Select(i => i.Id).ToList();
    }

    private Task<Guid> Person(string name, string? email) =>
        Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest(name, "Arama" + Guid.NewGuid().ToString("N")[..4], email, null, null), ct).Value());

    private Task<Guid> Gift(Guid contact, decimal amount, DateOnly on) =>
        Scoped<DonationService, Guid>(s => s.RecordAsync(new SaveDonationRequest(contact, amount, "TRY", on, null, PaymentMethod.Cash, null, null, null), ct).Value());

    private Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull =>
        factory.WithScopeAsync(sp => action(sp.GetRequiredService<TService>()));
}
