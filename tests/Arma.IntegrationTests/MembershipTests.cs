using Arma.Modules.Activities.Application;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Membership.Application;
using Arma.Modules.Membership.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class MembershipTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    [Fact]
    public void Status_follows_period_grace_and_cancellation()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 12, 31);
        Member.StatusOf(null, start, end, 30, new(2025, 12, 31)).ShouldBe(MembershipStatus.Pending);
        Member.StatusOf(null, start, end, 30, new(2026, 12, 31)).ShouldBe(MembershipStatus.Current);
        Member.StatusOf(null, start, end, 30, new(2027, 1, 30)).ShouldBe(MembershipStatus.Grace);
        Member.StatusOf(null, start, end, 30, new(2027, 1, 31)).ShouldBe(MembershipStatus.Expired);
        Member.StatusOf(null, start, null, 0, new(2090, 1, 1)).ShouldBe(MembershipStatus.Current);
        Member.StatusOf(new(2026, 6, 1), start, end, 30, new(2026, 6, 1)).ShouldBe(MembershipStatus.Cancelled);
        Member.StatusOf(new(2026, 6, 1), start, end, 30, new(2026, 5, 31)).ShouldBe(MembershipStatus.Current);
    }

    [Fact]
    public async Task Join_renew_and_cancel_keep_numbers_periods_and_dues()
    {
        RequiresDatabase.Check();
        var annual = await Scoped<MembershipService, Guid>(s => s.SaveTypeAsync(null, new SaveMembershipTypeRequest("Asıl üye", null, 12, 600m, 30, true), ct).Value());
        var honorary = await Scoped<MembershipService, Guid>(s => s.SaveTypeAsync(null, new SaveMembershipTypeRequest("Fahri üye", null, 0, 0m, 0, true), ct).Value());
        var ayse = await Person("Ayşe", "Üye");
        var mehmet = await Person("Mehmet", "Eskiüye");

        var first = await Scoped<MembershipService, Guid>(s => s.JoinAsync(new JoinRequest(ayse, annual, Today, null, new DuesInput(600m, Today, DuesMethod.Cash, null)), ct).Value());
        var duplicate = await factory.WithScopeAsync(sp => sp.GetRequiredService<MembershipService>().JoinAsync(new JoinRequest(ayse, annual, Today, null, null), ct));
        duplicate.Error.ShouldBe(MembershipErrors.AlreadyMember);
        var lifetime = await Scoped<MembershipService, Guid>(s => s.JoinAsync(new JoinRequest(ayse, honorary, Today, null, null), ct).Value());
        var lapsed = await Scoped<MembershipService, Guid>(s => s.JoinAsync(new JoinRequest(mehmet, annual, Today.AddYears(-2), null, null), ct).Value());

        var a = (await Get(first))!;
        a.ShouldSatisfyAllConditions(m => m.EndOn.ShouldBe(Today.AddYears(1).AddDays(-1)), m => m.Status.ShouldBe(MembershipStatus.Current));
        (await Get(lifetime))!.Number.ShouldBe(a.Number + 1);
        (await Get(lifetime))!.EndOn.ShouldBeNull();
        (await Get(lapsed))!.Status.ShouldBe(MembershipStatus.Expired);

        // Süresi içinde yenileme kesintisiz devam eder; süresi dolmuşsa bugünden başlar.
        await Scoped<MembershipService, Guid>(s => s.RenewAsync(first, new DuesInput(600m, Today, DuesMethod.BankTransfer, null), ct).Value());
        (await Get(first))!.ShouldSatisfyAllConditions(m => m.StartOn.ShouldBe(Today), m => m.EndOn.ShouldBe(Today.AddYears(2).AddDays(-1)));
        await Scoped<MembershipService, Guid>(s => s.RenewAsync(lapsed, null, ct).Value());
        (await Get(lapsed))!.ShouldSatisfyAllConditions(m => m.StartOn.ShouldBe(Today), m => m.Status.ShouldBe(MembershipStatus.Current), m => m.JoinedOn.ShouldBe(Today.AddYears(-2)));
        var lifetimeRenew = await factory.WithScopeAsync(sp => sp.GetRequiredService<MembershipService>().RenewAsync(lifetime, null, ct));
        lifetimeRenew.Error!.Code.ShouldBe("memberships.lifetime");

        var payments = await Scoped<MembershipService, IReadOnlyList<DuesView>>(s => s.PaymentsAsync(first, ct));
        payments.Sum(p => p.Amount).ShouldBe(1200m);
        payments.Single(p => p.Method == DuesMethod.BankTransfer).PeriodStart.ShouldBe(Today.AddYears(1));

        await Scoped<MembershipService, Guid>(s => s.CancelAsync(lapsed, Today, "İstifa", ct).Value());
        (await Get(lapsed))!.Status.ShouldBe(MembershipStatus.Cancelled);

        var current = await Scoped<MembershipService, MemberPage>(s => s.ListAsync(new MemberFilter(Status: MembershipStatus.Current), ct));
        current.Items.Select(m => m.Id).ShouldContain(first);
        current.Items.Select(m => m.Id).ShouldContain(lifetime);
        current.Items.Select(m => m.Id).ShouldNotContain(lapsed);

        var criteria = factory.Services.CreateScope().ServiceProvider.GetServices<IContactCriteriaProvider>().OfType<MembershipInsights>().Single();
        (await criteria.MatchAsync("member_status", "Cancelled", ct))!.ContactIds.ShouldBe([mehmet]);

        var timeline = await EventuallyAsync(
            () => factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>().TimelineAsync(ayse, cancellationToken: ct)),
            t => t.History.Count(a => a.Subject.StartsWith("Aidat alındı")) == 2);
        timeline.History.ShouldContain(a => a.Subject.StartsWith("Üye oldu: Asıl üye"));
        timeline.History.ShouldContain(a => a.Subject.StartsWith("Üyelik yenilendi"));
    }

    [Fact]
    public async Task Renewal_reminder_is_sent_once_per_period()
    {
        RequiresDatabase.Check();
        var type = await Scoped<MembershipService, Guid>(s => s.SaveTypeAsync(null, new SaveMembershipTypeRequest("Aylık üye", null, 1, 50m, 10, true), ct).Value());
        var contact = await Person("Hatırlatılacak", "Üye");
        var member = await Scoped<MembershipService, Guid>(s => s.JoinAsync(new JoinRequest(contact, type, Today.AddDays(-20), null, null), ct).Value());

        (await Scoped<RenewalReminderJob, int>(j => j.SendAsync(ct))).ShouldBe(1);
        (await Scoped<RenewalReminderJob, int>(j => j.SendAsync(ct))).ShouldBe(0);

        await Scoped<MembershipService, Guid>(s => s.RenewAsync(member, null, ct).Value());
        (await Scoped<RenewalReminderJob, int>(j => j.SendAsync(ct))).ShouldBe(0, "yeni dönem henüz 30 gün içinde bitmiyor sayılmaz; bitiş yaklaşınca tekrar gönderilir");
    }

    private Task<MemberView?> Get(Guid id) => Scoped<MembershipService, MemberView?>(s => s.GetAsync(id, ct));

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
