using System.Net;
using Arma.Modules.Comms.Senders;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.Payments;
using Arma.Payments.Providers;
using Arma.SharedKernel.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace Arma.IntegrationTests;

public sealed class PaymentTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Built_in_and_plugin_providers_are_discovered_with_their_settings_state()
    {
        var gateway = factory.Services.CreateScope().ServiceProvider.GetRequiredService<IPaymentGateway>();

        gateway.Providers.Select(p => p.Key).ShouldBe(["iyzico", "paytr", "test", "ornek"]);
        gateway.Find("iyzico")!.MissingSettings.Select(s => s.Key).ShouldBe(["ApiKey", "SecretKey"]);
        gateway.Find("paytr")!.IsConfigured.ShouldBeFalse();
        gateway.Find("test")!.IsConfigured.ShouldBeTrue();
        // Örnek eklentideki sağlayıcı Arma'nın kodunda adı geçmeden bulunur ve ayarı verilince hazır olur.
        gateway.Find("ornek")!.IsConfigured.ShouldBeTrue();
        gateway.Find("iyzico")!.Descriptor.Supports(PaymentCapabilities.StoredCard).ShouldBeTrue();
        gateway.Find("paytr")!.Descriptor.Supports(PaymentCapabilities.StoredCard).ShouldBeFalse();
    }

    [Fact]
    public void Request_builder_reports_every_missing_field()
    {
        var error = Should.Throw<InvalidOperationException>(() => PaymentRequestBuilder.For("test").Order("ab-1").Amount(10.555m).Build());
        error.Message.ShouldContain("sipariş numarası");
        error.Message.ShouldContain("tutar");
        error.Message.ShouldContain("açıklama");
        error.Message.ShouldContain("ödeyen");
        error.Message.ShouldContain("dönüş adresleri");

        var request = PaymentRequestBuilder.For("test").Order(Guid.NewGuid()).Amount(25m).Description("Bağış")
            .Payer(new Payer("Ada", "Yılmaz", "ada@example.org", null, "127.0.0.1"))
            .ReturnTo("https://crm.example.org/cb", "https://crm.example.org/ok", "https://crm.example.org/fail")
            .StoreCard().Build();
        request.StoreCard.ShouldBeTrue();
        request.Currency.ShouldBe("TRY");
    }

    [Fact]
    public async Task Chain_runs_handlers_in_order_and_a_handler_can_stop_the_payment()
    {
        await using var services = Services(b => b.AddHandler<PaymentStartContext, PaymentStart, LimitHandler>());
        await using var scope = services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();

        (await gateway.StartAsync(Request("test", 60_000m), ct)).Error.ShouldBe("Sınır aşıldı");
        (await gateway.StartAsync(Request("test", 100m), ct)).RedirectUrl.ShouldStartWith("/bagis/test-odeme");
        (await gateway.StartAsync(Request("yok", 100m), ct)).Error.ShouldBe("Bu ödeme yöntemi şu an kullanılamıyor.");
        // Ayarı eksik sağlayıcıya istek gitmez.
        (await gateway.StartAsync(Request("iyzico", 100m), ct)).Error.ShouldBe("Bu ödeme yöntemi şu an kullanılamıyor.");
        // PayTR kart saklamadığı için düzenli bağış isteği zincirde durur.
        await using var configured = Services(configure: c => c.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payments:Providers:paytr:MerchantId"] = "1",
            ["Payments:Providers:paytr:MerchantKey"] = Guid.NewGuid().ToString("N"),
            ["Payments:Providers:paytr:MerchantSalt"] = Guid.NewGuid().ToString("N"),
        }));
        await using var configuredScope = configured.CreateAsyncScope();
        var paytr = configuredScope.ServiceProvider.GetRequiredService<IPaymentGateway>();
        (await paytr.StartAsync(Request("paytr", 100m) with { StoreCard = true }, ct)).Error.ShouldBe("Bu ödeme yöntemiyle düzenli bağış yapılamıyor.");
        (await paytr.ChargeStoredCardAsync(new StoredCardCharge("paytr", "abc", 10m, "TRY", "Bağış", Payer, new StoredCard("x", null)), ct))
            .Error.ShouldBe("Bu ödeme yöntemi kayıtlı karttan tahsilatı desteklemiyor.");
    }

    [Fact]
    public void Development_only_providers_are_hidden_in_production()
    {
        using var services = Services(environment: Environments.Production);
        services.GetRequiredService<IPaymentProviderFactory>().All.Select(h => h.Descriptor.Key).ShouldNotContain("test");
    }

    [Fact]
    public void Iyzico_signs_requests_and_reads_stored_card()
    {
        var key = Guid.NewGuid().ToString("N");
        var settings = new FixedSettings(new() { ["ApiKey"] = "api", ["SecretKey"] = key });
        var header = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(IyzicoProvider.Authorization(settings, "rnd", "/p", "{}")));
        // HMACSHA256(gizli anahtar, "rnd/p{}") onaltılık.
        header.ShouldBe("apiKey:api&randomKey:rnd&signature:" + Convert.ToHexStringLower(
            System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key), "rnd/p{}"u8.ToArray())));

        var card = IyzicoProvider.Card(System.Text.Json.Nodes.JsonNode.Parse("""{"cardUserKey":"u1","cardToken":"t1","lastFourDigits":"0008","cardAssociation":"MASTER_CARD"}""")!);
        card.ShouldBe(new StoredCard("u1:t1", "MASTER CARD **** 0008"));
        IyzicoProvider.Card(System.Text.Json.Nodes.JsonNode.Parse("""{"paymentId":"1"}""")!).ShouldBeNull();
    }

    [Fact]
    public async Task Offered_providers_follow_the_saved_choice_and_order()
    {
        RequiresDatabase.Check();
        var online = (Func<OnlineGivingService, Task> action) => factory.WithScopeAsync(async sp => { await action(sp.GetRequiredService<OnlineGivingService>()); return true; });

        IReadOnlyList<PaymentOption> options = [];
        await online(async s => options = await s.OptionsAsync(ct));
        options.Select(o => o.Key).ShouldBe(["test", "ornek"]);

        await online(async s => (await s.SaveOfferedProvidersAsync(["ornek", "iyzico"], ct)).IsSuccess.ShouldBeTrue());
        await online(async s => options = await s.OptionsAsync(ct));
        // iyzico işaretli ama ayarı eksik: formda görünmez.
        options.Select(o => o.Key).ShouldBe(["ornek"]);
        await online(async s => (await s.ProvidersAsync(ct)).Select(p => (p.Info.Key, p.IsOffered)).ShouldBe([("ornek", true), ("iyzico", true), ("paytr", false), ("test", false)]));
        await online(async s => (await s.SaveOfferedProvidersAsync(["bilinmeyen"], ct)).IsSuccess.ShouldBeFalse());

        // Eklentideki sağlayıcıyla baştan sona bağış.
        var start = await factory.WithScopeAsync(sp => sp.GetRequiredService<OnlineGivingService>()
            .StartAsync(new OnlineDonationRequest("Ek", "Lenti", "eklenti@example.org", null, 75m, null), "127.0.0.1", new OnlineGivingUrls("http://localhost"), ct));
        start.Error.ShouldBeNull();
        start.Value!.Provider.ShouldBe("ornek");
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri("http://localhost" + start.Value.RedirectUrl).Query);
        (await client.GetAsync(start.Value.RedirectUrl, ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var paid = await client.PostAsync("/api/v1/payments/ornek/callback", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["order"] = query["order"]!, ["amount"] = query["amount"]!, ["cb"] = query["cb"]!, ["sig"] = query["sig"]!,
        }), ct);
        paid.Headers.Location!.ToString().ShouldContain("/bagis/tesekkurler");
        (await factory.WithScopeAsync(async sp => (await sp.GetRequiredService<OnlineGivingService>().StatusAsync(start.Value.DonationId, ct))?.Status))
            .ShouldBe(DonationStatus.Completed);

        // Başka sağlayıcının adresine gelen bildirim bu bağışı tamamlayamaz.
        await online(async s => (await s.SaveOfferedProvidersAsync(["test", "ornek"], ct)).IsSuccess.ShouldBeTrue());
    }

    [Fact]
    public async Task Monthly_donation_stores_the_card_charges_each_month_and_pauses_after_failures()
    {
        RequiresDatabase.Check();
        var urls = new OnlineGivingUrls("http://localhost");
        var start = await factory.WithScopeAsync(sp => sp.GetRequiredService<OnlineGivingService>()
            .StartAsync(new OnlineDonationRequest("Aylık", "Bağışçı", "aylik@example.org", null, 200m, null) { Provider = "test", Monthly = true }, "127.0.0.1", urls, ct));
        start.Error.ShouldBeNull();
        start.Value!.RedirectUrl!.ShouldContain("store=1");
        var order = start.Value.DonationId.ToString("N");

        var signature = await factory.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<TestPaymentProvider>().Sign(order, "200", "success", "1")));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var paid = await client.PostAsync("/api/v1/payments/test/callback", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["order"] = order, ["amount"] = "200", ["status"] = "success", ["store"] = "1", ["sig"] = signature,
        }), ct);
        paid.Headers.Location!.ToString().ShouldContain("/bagis/tesekkurler");

        var plan = (await Recurring(s => s.ListAsync(cancellationToken: ct))).ShouldHaveSingleItem();
        plan.Status.ShouldBe(RecurringStatus.Active);
        plan.CardLabel.ShouldBe("TEST **** 4242");
        plan.PaymentCount.ShouldBe(1);
        plan.NextChargeOn.ShouldNotBeNull().ShouldBeGreaterThan(DateOnly.FromDateTime(DateTime.Now));

        // Kart jetonu şifreli saklanır.
        var stored = await factory.WithScopeAsync(sp => sp.GetRequiredService<GivingDbContext>().RecurringDonations.AsNoTracking().Select(r => r.CardToken).SingleAsync(ct));
        stored.ShouldNotBeNull().ShouldNotContain("test-card-");

        // Günü gelince kayıtlı karttan çekilir.
        await MakeDueAsync();
        (await Recurring(s => s.ChargeDueAsync(ct))).ShouldBe(new RecurringRunResult(1, 0, 0));
        (await Recurring(s => s.ChargeDueAsync(ct))).ShouldBe(new RecurringRunResult(0, 0, 0));
        plan = (await Recurring(s => s.ListAsync(cancellationToken: ct))).Single();
        (plan.PaymentCount, plan.PaidTotal).ShouldBe((2, 400m));

        // Üst üste üç başarısız tahsilatta durur ve bağışçıya haber verilir.
        var provider = factory.Services.GetRequiredService<TestPaymentProvider>();
        provider.DeclineCharges = true;
        try
        {
            for (var attempt = 1; attempt <= RecurringDonation.MaxFailures; attempt++)
            {
                await MakeDueAsync();
                (await Recurring(s => s.ChargeDueAsync(ct))).ShouldBe(attempt < RecurringDonation.MaxFailures ? new RecurringRunResult(0, 1, 0) : new RecurringRunResult(0, 0, 1));
            }
        }
        finally
        {
            provider.DeclineCharges = false;
        }

        plan = (await Recurring(s => s.ListAsync(cancellationToken: ct))).Single();
        plan.Status.ShouldBe(RecurringStatus.Paused);
        plan.LastError.ShouldBe("Test kartı reddedildi");
        var sink = factory.Services.GetRequiredService<TestMessageSink>();
        (await EventuallyAsync(() => sink.Sent.Any(m => m.To == "aylik@example.org" && m.Subject == "Düzenli bağışınız durdu"))).ShouldBeTrue();

        // Yeniden başlatılabilir; durdurulunca kart jetonu silinir.
        (await Recurring(s => s.ResumeAsync(plan.Id, ct))).IsSuccess.ShouldBeTrue();
        (await Recurring(s => s.ChargeDueAsync(ct))).ShouldBe(new RecurringRunResult(1, 0, 0));
        (await Recurring(s => s.CancelAsync(plan.Id, Guid.NewGuid(), ct))).IsSuccess.ShouldBeFalse();
        (await Recurring(s => s.CancelAsync(plan.Id, plan.ContactId, ct))).IsSuccess.ShouldBeTrue();
        (await factory.WithScopeAsync(sp => sp.GetRequiredService<GivingDbContext>().RecurringDonations.AsNoTracking().Select(r => r.CardToken).SingleAsync(ct))).ShouldBeNull();
    }

    [Fact]
    public async Task Monthly_donation_is_not_started_when_the_first_payment_fails()
    {
        RequiresDatabase.Check();
        var start = await factory.WithScopeAsync(sp => sp.GetRequiredService<OnlineGivingService>()
            .StartAsync(new OnlineDonationRequest("Red", "Kart", "red@example.org", null, 50m, null) { Provider = "test", Monthly = true }, "127.0.0.1", new OnlineGivingUrls("http://localhost"), ct));
        var order = start.Value!.DonationId.ToString("N");
        var signature = await factory.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<TestPaymentProvider>().Sign(order, "50", "failed", "1")));
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        await client.PostAsync("/api/v1/payments/test/callback", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["order"] = order, ["amount"] = "50", ["status"] = "failed", ["store"] = "1", ["sig"] = signature,
        }), ct);

        var row = await factory.WithScopeAsync(sp => sp.GetRequiredService<GivingDbContext>().RecurringDonations.AsNoTracking()
            .Where(r => r.Amount == 50m).Select(r => new { r.Status, r.NextChargeOn }).SingleAsync(ct));
        row.Status.ShouldBe(RecurringStatus.Cancelled);
        row.NextChargeOn.ShouldBeNull();

        // Ödeme sayfası yalnızca bekleyen bağışın sağlayıcısının adresini çerçeveler.
        (await factory.WithScopeAsync(sp => sp.GetRequiredService<OnlineGivingService>().AllowsFrameAsync(start.Value.DonationId, "https://www.paytr.com/odeme/guvenli/x", ct))).ShouldBeFalse();
    }

    private static readonly Payer Payer = new("Ada", "Yılmaz", "ada@example.org", null, "127.0.0.1");

    private static PaymentRequest Request(string provider, decimal amount) =>
        PaymentRequestBuilder.For(provider).Order(Guid.NewGuid()).Amount(amount).Description("Bağış").Payer(Payer)
            .ReturnTo("https://crm.example.org/cb", "https://crm.example.org/ok", "https://crm.example.org/fail").Build();

    private static ServiceProvider Services(Action<PaymentsBuilder>? build = null, Action<IConfigurationBuilder>? configure = null, string environment = "Testing")
    {
        var configuration = new ConfigurationBuilder();
        configure?.Invoke(configuration);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddSingleton<IConfiguration>(configuration.Build());
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment });
        var builder = services.AddArmaPayments();
        build?.Invoke(builder);
        return services.BuildServiceProvider();
    }

    private Task MakeDueAsync() => factory.WithScopeAsync(sp => sp.GetRequiredService<GivingDbContext>().RecurringDonations
        .ExecuteUpdateAsync(u => u.SetProperty(r => r.NextChargeOn, DateOnly.FromDateTime(DateTime.Now)), ct));

    private Task<T> Recurring<T>(Func<RecurringDonationService, Task<T>> action) =>
        factory.WithScopeAsync(sp => action(sp.GetRequiredService<RecurringDonationService>()));

    private async Task<bool> EventuallyAsync(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!done() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250, ct);
        }

        return done();
    }

    private sealed class LimitHandler : IPaymentHandler<PaymentStartContext, PaymentStart>
    {
        public int Order => 150;

        public Task<PaymentStart> HandleAsync(PaymentStartContext context, Func<Task<PaymentStart>> next, CancellationToken cancellationToken) =>
            context.Request.Amount > 50_000m ? Task.FromResult(PaymentStart.Failed("Sınır aşıldı")) : next();
    }

    private sealed class FixedSettings(Dictionary<string, string> values) : IPaymentProviderSettings
    {
        public string ProviderKey => "fixed";

        public string this[string field] => values.GetValueOrDefault(field, "");

        public IReadOnlyList<PaymentSettingField> Missing => [];
    }
}
