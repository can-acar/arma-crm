using System.Net;
using System.Text.RegularExpressions;
using Arma.Modules.Comms.Senders;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Identity.Application;
using Arma.Modules.Portal.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed partial class PortalTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Magic_link_signs_the_donor_in_once_and_the_portal_shows_only_their_own_receipts()
    {
        RequiresDatabase.Check();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"portal{suffix}@example.org";
        var donor = await Person("Portal", "Bağışçı" + suffix[..3], email);
        var other = await Person("Başka", "Kişi" + suffix[..3], null);
        var mine = await Donate(donor, 250m);
        var theirs = await Donate(other, 999m);
        var mineNumber = await Scoped<DonationService, string>(s => s.IssueReceiptAsync(mine, ct).Value());
        var theirsNumber = await Scoped<DonationService, string>(s => s.IssueReceiptAsync(theirs, ct).Value());

        (await Scoped<PortalService, bool>(s => s.RequestLinkAsync("yok" + email, ct))).ShouldBeFalse();
        (await Scoped<PortalService, bool>(s => s.RequestLinkAsync(email.ToUpperInvariant(), ct))).ShouldBeTrue();
        (await Scoped<PortalService, bool>(s => s.RequestLinkAsync(email, ct))).ShouldBeFalse(); // bir dakika içinde ikinci bağlantı gitmez

        var mail = await EventuallyAsync(() => Task.FromResult(Sink.Sent.FirstOrDefault(m => m.To == email)), m => m.Body is not null);
        var link = LinkPattern().Match(mail.Body).Groups[1].Value;
        link.ShouldNotBeEmpty();
        var token = Uri.UnescapeDataString(link);
        (await Scoped<PortalService, Guid?>(s => Task.FromResult(s.ReadLink(token)))).ShouldBe(donor);
        (await Scoped<PortalService, Guid?>(s => Task.FromResult(s.ReadLink(token + "x")))).ShouldBeNull();

        // Kurulum sihirbazı tamamlanmadan her sayfa /setup'a yönlenir; bu test kullanıcı oluşturmaz.
        factory.Services.GetRequiredService<SetupState>().MarkCompleted();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var anonymous = await client.GetAsync("/portal/hesap", ct);
        (anonymous.StatusCode, anonymous.Headers.Location?.ToString()).ShouldBe((HttpStatusCode.Redirect, "http://localhost/portal"));

        var confirm = await client.GetStringAsync($"/portal/giris?t={link}", ct);
        var form = new Dictionary<string, string>
        {
            ["_handler"] = "portal-confirm",
            ["__RequestVerificationToken"] = AntiforgeryPattern().Match(confirm).Groups[1].Value,
            ["Token"] = token,
        };
        var signIn = await client.PostAsync("/portal/giris", new FormUrlEncodedContent(form), ct);
        signIn.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        cookies!.ShouldContain(c => c.StartsWith(PortalService.CookieName + "="));

        var accountResponse = await client.GetAsync("/portal/hesap", ct);
        accountResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var account = WebUtility.HtmlDecode(await accountResponse.Content.ReadAsStringAsync(ct));
        account.ShouldContain("Merhaba, Portal Bağışçı" + suffix[..3]);
        account.ShouldContain("250,00 TL");
        account.ShouldContain($"portal/makbuz/{mine}");
        account.ShouldNotContain(theirs.ToString());

        (await client.GetStringAsync($"/portal/makbuz/{mine}", ct)).ShouldContain(mineNumber);
        (await client.GetStringAsync($"/portal/makbuz/{theirs}", ct)).ShouldNotContain(theirsNumber);

        // Bağlantı ikinci kez kullanılamaz.
        (await Scoped<PortalService, Guid?>(s => Task.FromResult(s.ReadLink(token)))).ShouldBeNull();
        using var stranger = factory.CreateClient(new() { AllowAutoRedirect = false });
        WebUtility.HtmlDecode(await stranger.GetStringAsync($"/portal/giris?t={link}", ct)).ShouldContain("daha önce kullanılmış");
        (await stranger.GetStringAsync($"/portal/makbuz/{mine}", ct)).ShouldNotContain(mineNumber);
    }

    [GeneratedRegex(@"/portal/giris\?t=(\S+)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryPattern();

    private TestMessageSink Sink => factory.Services.GetRequiredService<TestMessageSink>();

    private Task<Guid> Person(string first, string last, string? email) =>
        Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest(first, last, email, null, null), ct).Value());

    private Task<Guid> Donate(Guid contact, decimal amount) =>
        factory.WithScopeAsync(sp => sp.GetRequiredService<DonationService>().RecordAsync(
            new SaveDonationRequest(contact, amount, "TRY", DateOnly.FromDateTime(DateTime.Today), null, PaymentMethod.Cash, null, null, null), ct)).Value();

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
