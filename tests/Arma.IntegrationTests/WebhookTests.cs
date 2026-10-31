using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Webhooks.Application;
using Arma.Modules.Webhooks.Domain;
using Arma.SharedKernel.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class WebhookTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Endpoint_requires_https_except_loopback_and_a_known_event()
    {
        WebhookEndpoint.Create("A", "http://ornek.org/hook", ["giving.activity"], true, "x", false).Error!.Code.ShouldBe("webhooks.https");
        WebhookEndpoint.Create("A", "http://ornek.org/hook", ["giving.activity"], true, "x", true).IsSuccess.ShouldBeTrue();
        WebhookEndpoint.Create("A", "http://127.0.0.1:9/hook", ["giving.activity"], true, "x", false).IsSuccess.ShouldBeTrue();
        WebhookEndpoint.Create("A", "ftp://ornek.org", ["giving.activity"], true, "x", false).Error!.Code.ShouldBe("webhooks.url");
        WebhookEndpoint.Create("A", "https://ornek.org", ["bilinmeyen"], true, "x", false).Error!.Code.ShouldBe("webhooks.events");
        WebhookService.Sign("gizli", "1700000000", "{}").ShouldBe("sha256=" + Convert.ToHexStringLower(
            System.Security.Cryptography.HMACSHA256.HashData("gizli"u8.ToArray(), "1700000000.{}"u8.ToArray())));
    }

    [Fact]
    public async Task Donation_is_delivered_signed_and_failed_delivery_is_scheduled_for_retry()
    {
        RequiresDatabase.Check();
        using var receiver = new Receiver();
        var created = await Scoped<WebhookService, EndpointSecret>(s => s.CreateAsync(new SaveEndpointRequest("Muhasebe", receiver.Url, ["giving.activity"], true), ct).Value());
        created.Secret.ShouldStartWith("whsec_");

        var ping = await Scoped<WebhookService, WebhookTestResult>(s => s.TestAsync(created.Id, ct));
        ping.Success.ShouldBeTrue();
        receiver.Requests.ShouldContain(r => r.Event == "ping");

        var donor = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Webhook", "Bağışçı", "webhook@example.org", null, null), ct).Value());
        var donation = await Scoped<DonationService, Guid>(s => s.RecordAsync(new SaveDonationRequest(donor, 750m, "TRY", DateOnly.FromDateTime(DateTime.Today), null, PaymentMethod.Cash, null, null, null), ct).Value());

        // Paralel çalışan testlerin bağışları da bu adrese düşebilir; kendi bağışımızı arıyoruz.
        bool Ours(string body) => body.Contains(donation.ToString());
        var queued = await EventuallyAsync(() => Scoped<WebhookService, IReadOnlyList<DeliveryView>>(s => s.DeliveriesAsync(created.Id, cancellationToken: ct)), l => l.Any(d => Ours(d.Payload)));
        var mine = queued.Single(d => Ours(d.Payload));
        mine.Status.ShouldBe(DeliveryStatus.Pending);
        (await Scoped<WebhookService, int>(s => s.DeliverDueAsync(ct))).ShouldBeGreaterThanOrEqualTo(1);

        var request = receiver.Requests.Single(r => Ours(r.Body));
        request.Event.ShouldBe("giving.activity");
        request.Signature.ShouldBe(WebhookService.Sign(created.Secret, request.Timestamp, request.Body));
        using var json = JsonDocument.Parse(request.Body);
        var data = json.RootElement.GetProperty("data");
        data.GetProperty("source").GetProperty("id").GetString().ShouldBe(donation.ToString());
        data.GetProperty("contacts")[0].GetProperty("email").GetString().ShouldBe("webhook@example.org");
        json.RootElement.GetProperty("id").GetString().ShouldBe(request.Delivery);
        (await Scoped<WebhookService, IReadOnlyList<DeliveryView>>(s => s.DeliveriesAsync(created.Id, cancellationToken: ct))).Single(d => d.Id == mine.Id).Status.ShouldBe(DeliveryStatus.Succeeded);

        // Kapalı porta gönderim başarısız olur ve bir dakika sonraya ertelenir.
        var closed = await Scoped<WebhookService, EndpointSecret>(s => s.CreateAsync(new SaveEndpointRequest("Kapalı", $"http://127.0.0.1:{FreePort()}/hook", ["giving.activity"], true), ct).Value());
        var second = await Scoped<DonationService, Guid>(s => s.RecordAsync(new SaveDonationRequest(donor, 10m, "TRY", DateOnly.FromDateTime(DateTime.Today), null, PaymentMethod.Cash, null, null, null), ct).Value());
        await EventuallyAsync(() => Scoped<WebhookService, IReadOnlyList<DeliveryView>>(s => s.DeliveriesAsync(closed.Id, cancellationToken: ct)), l => l.Any(d => d.Payload.Contains(second.ToString())));
        await Scoped<WebhookService, int>(s => s.DeliverDueAsync(ct));
        var failed = (await Scoped<WebhookService, IReadOnlyList<DeliveryView>>(s => s.DeliveriesAsync(closed.Id, cancellationToken: ct))).Single(d => d.Payload.Contains(second.ToString()));
        (failed.Status, failed.Attempts, failed.LastError is not null).ShouldBe((DeliveryStatus.Pending, 1, true));
        failed.NextAttemptAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddSeconds(30));

        await Scoped<WebhookService, Guid>(s => s.DeleteAsync(closed.Id, ct).Value());
        await Scoped<WebhookService, Guid>(s => s.DeleteAsync(created.Id, ct).Value());
        (await Scoped<WebhookService, IReadOnlyList<EndpointView>>(s => s.ListAsync(ct))).ShouldNotContain(e => e.Id == created.Id);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
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

    private sealed record Received(string Event, string Delivery, string Timestamp, string Signature, string Body);

    /// <summary>Gelen webhook isteklerini toplayan yerel HTTP sunucusu.</summary>
    private sealed class Receiver : IDisposable
    {
        private readonly HttpListener listener = new();

        public Receiver()
        {
            Url = $"http://127.0.0.1:{FreePort()}/hook/";
            listener.Prefixes.Add(Url);
            listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public string Url { get; }

        public ConcurrentQueue<Received> Requests { get; } = new();

        public void Dispose() => listener.Close();

        private async Task LoopAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception)
                {
                    return;
                }

                using var reader = new StreamReader(context.Request.InputStream);
                var body = await reader.ReadToEndAsync();
                Requests.Enqueue(new Received(context.Request.Headers["X-Arma-Event"]!, context.Request.Headers["X-Arma-Delivery"]!,
                    context.Request.Headers["X-Arma-Timestamp"]!, context.Request.Headers["X-Arma-Signature"]!, body));
                context.Response.StatusCode = 204;
                context.Response.Close();
            }
        }
    }
}
