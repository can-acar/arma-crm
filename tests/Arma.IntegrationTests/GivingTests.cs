using System.Net;
using Arma.Modules.Activities.Application;
using Arma.Modules.Comms.Senders;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Domain;
using Arma.Payments.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class GivingTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    [Fact]
    public async Task Recorded_donation_updates_totals_and_timeline()
    {
        RequiresDatabase.Check();
        var donor = await NewPersonAsync("Hasan", "Çelik");
        var fund = await Scoped<FundService, Guid>(s => s.CreateAsync(new SaveFundRequest("Burs Fonu", null, true, true), ct).Value());
        await Scoped<DonationService, Guid>(s => s.RecordAsync(Request(donor, 1250.50m) with { FundId = fund }, ct).Value());
        await Scoped<DonationService, Guid>(s => s.RecordAsync(Request(donor, 100m) with { Currency = "usd" }, ct).Value());

        var summary = await Scoped<DonationService, DonorSummary>(s => s.DonorSummaryAsync(donor, ct));
        summary.Lifetime.Select(t => (t.Currency, t.Amount)).ShouldBe([("TRY", 1250.50m), ("USD", 100m)]);
        summary.LargestTry.ShouldBe(1250.50m);

        var history = await EventuallyAsync(
            () => Scoped<ActivityService, IReadOnlyList<ActivityView>>(async s => (await s.TimelineAsync(donor, cancellationToken: ct)).History),
            h => h.Count == 2);
        history.ShouldContain(a => a.Subject == "Bağış: 1.250,50 TL (Burs Fonu)" && a.IsSystem);
    }

    [Fact]
    public async Task Receipts_are_numbered_sequentially_and_lock_the_amount()
    {
        RequiresDatabase.Check();
        await factory.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<DonationService>().SaveReceiptSettingsAsync(new ReceiptSettings { OrganizationName = "Umut Derneği", NumberPrefix = "mkb-" }, ct);
            return true;
        });
        var donor = await NewPersonAsync("Elif", "Aydın");
        var first = await Scoped<DonationService, Guid>(s => s.RecordAsync(Request(donor, 50m), ct).Value());
        var second = await Scoped<DonationService, Guid>(s => s.RecordAsync(Request(donor, 75m), ct).Value());

        var n1 = await Scoped<DonationService, string>(s => s.IssueReceiptAsync(first, ct).Value());
        var n2 = await Scoped<DonationService, string>(s => s.IssueReceiptAsync(second, ct).Value());
        var year = DateTime.Now.Year;
        n1.ShouldStartWith($"MKB-{year}-");
        long.Parse(n2[^6..]).ShouldBe(long.Parse(n1[^6..]) + 1);

        var again = await factory.WithScopeAsync(sp => sp.GetRequiredService<DonationService>().IssueReceiptAsync(first, ct));
        again.Error!.Code.ShouldBe("donations.receipt_exists");

        var change = await factory.WithScopeAsync(sp => sp.GetRequiredService<DonationService>().UpdateAsync(first, Request(donor, 60m), ct));
        change.Error!.Code.ShouldBe("donations.receipt_issued");

        var delete = await factory.WithScopeAsync(sp => sp.GetRequiredService<DonationService>().DeleteAsync(first, ct));
        delete.Error!.Code.ShouldBe("donations.has_receipt");

        await Scoped<DonationService, Guid>(s => s.VoidReceiptAsync(first, "Yanlış kişi", ct).Value());
        await Scoped<DonationService, Guid>(s => s.UpdateAsync(first, Request(donor, 60m), ct).Value());
        var reissued = await Scoped<DonationService, string>(s => s.IssueReceiptAsync(first, ct).Value());
        long.Parse(reissued[^6..]).ShouldBe(long.Parse(n2[^6..]) + 1);
    }

    [Fact]
    public async Task Refund_voids_receipt_and_leaves_totals()
    {
        RequiresDatabase.Check();
        var donor = await NewPersonAsync("İade", "Deneme");
        var id = await Scoped<DonationService, Guid>(s => s.RecordAsync(Request(donor, 200m), ct).Value());
        await Scoped<DonationService, string>(s => s.IssueReceiptAsync(id, ct).Value());

        await Scoped<DonationService, Guid>(s => s.RefundAsync(id, "Mükerrer çekim", ct).Value());

        var donation = await factory.WithScopeAsync(sp => sp.GetRequiredService<DonationService>().GetAsync(id, ct));
        donation!.Status.ShouldBe(DonationStatus.Refunded);
        donation.HasReceipt.ShouldBeFalse();
        (await Scoped<DonationService, DonorSummary>(s => s.DonorSummaryAsync(donor, ct))).Lifetime.ShouldBeEmpty();

        var history = await EventuallyAsync(
            () => Scoped<ActivityService, IReadOnlyList<ActivityView>>(async s => (await s.TimelineAsync(donor, cancellationToken: ct)).History),
            h => h.Any(a => a.Subject.StartsWith("İade edildi")));
        history.ShouldHaveSingleItem().Subject.ShouldStartWith("İade edildi");
    }

    [Fact]
    public async Task Merging_contacts_moves_donations()
    {
        RequiresDatabase.Check();
        var primary = await NewPersonAsync("Birleşen", "Bağışçı");
        var duplicate = await NewPersonAsync("Birleşen", "Bağışçı");
        await Scoped<DonationService, Guid>(s => s.RecordAsync(Request(duplicate, 30m), ct).Value());

        await Scoped<ContactService, Guid>(s => s.MergeAsync(primary, duplicate, ct).Value());

        var summary = await EventuallyAsync(() => Scoped<DonationService, DonorSummary>(s => s.DonorSummaryAsync(primary, ct)), s => s.Lifetime.Count > 0);
        summary.Lifetime.ShouldHaveSingleItem().Amount.ShouldBe(30m);
    }

    [Fact]
    public async Task Online_donation_completes_only_with_valid_callback()
    {
        RequiresDatabase.Check();
        var urls = new OnlineGivingUrls("http://localhost");
        var start = await factory.WithScopeAsync(sp => sp.GetRequiredService<OnlineGivingService>()
            .StartAsync(new OnlineDonationRequest("Çevrim", "İçi", "online@example.org", null, 150m, null), "127.0.0.1", urls, ct));
        start.Error.ShouldBeNull();
        var order = start.Value!.DonationId.ToString("N");
        start.Value.RedirectUrl.ShouldStartWith("/bagis/test-odeme");

        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var forged = await client.PostAsync("/api/v1/payments/test/callback", Form(order, "150", "success", "sahte"), ct);
        forged.Headers.Location!.ToString().ShouldContain("/bagis/basarisiz");
        (await StatusAsync(start.Value.DonationId)).ShouldBe(DonationStatus.Pending);

        var signature = await factory.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<TestPaymentProvider>().Sign(order, "150", "success")));
        var paid = await client.PostAsync("/api/v1/payments/test/callback", Form(order, "150", "success", signature), ct);
        paid.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        paid.Headers.Location!.ToString().ShouldContain("/bagis/tesekkurler");
        (await StatusAsync(start.Value.DonationId)).ShouldBe(DonationStatus.Completed);

        // Aynı bildirim tekrar gelirse sonuç değişmez.
        var replay = await client.PostAsync("/api/v1/payments/test/callback", Form(order, "150", "success", signature), ct);
        replay.Headers.Location!.ToString().ShouldContain("/bagis/tesekkurler");

        // Teşekkür e-postası yalnızca bir kez gider.
        var sink = factory.Services.GetRequiredService<TestMessageSink>();
        var thanks = await EventuallyAsync(() => Task.FromResult(sink.Sent.Where(m => m.To == "online@example.org").ToList()), m => m.Count > 0);
        await Task.Delay(1000, ct);
        sink.Sent.Count(m => m.To == "online@example.org").ShouldBe(1);
        thanks.Single().Body.ShouldContain("150,00 TL tutarındaki bağışınız");
    }

    [Fact]
    public void Amount_is_written_in_turkish_words()
    {
        Money.InWords(1250.50m, "TRY").ShouldBe("bin iki yüz elli Türk lirası elli kuruş");
        Money.InWords(2_001_000m, "TRY").ShouldBe("iki milyon bin Türk lirası");
        Money.InWords(101m, "USD").ShouldBe("yüz bir ABD doları");
        Money.Format(1250.5m, "TRY").ShouldBe("1.250,50 TL");
    }

    private Task<DonationStatus?> StatusAsync(Guid id) =>
        factory.WithScopeAsync(async sp => (await sp.GetRequiredService<OnlineGivingService>().StatusAsync(id, ct))?.Status);

    private static FormUrlEncodedContent Form(string order, string amount, string status, string sig) =>
        new(new Dictionary<string, string> { ["order"] = order, ["amount"] = amount, ["status"] = status, ["sig"] = sig });

    private static SaveDonationRequest Request(Guid donor, decimal amount) =>
        new(donor, amount, "TRY", Today, null, PaymentMethod.BankTransfer, null, null, null);

    private Task<Guid> NewPersonAsync(string first, string last) =>
        Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest(first, last, null, null, null), ct).Value());

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
