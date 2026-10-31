using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Webhooks.Domain;
using Arma.Modules.Webhooks.Infrastructure;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Arma.Modules.Webhooks.Application;

public sealed record EndpointView(Guid Id, string Name, string Url, IReadOnlyList<string> Events, bool IsActive, int Pending, int Failed, DateTimeOffset? LastDeliveredAt);

public sealed record SaveEndpointRequest(string Name, string Url, IReadOnlyList<string> Events, bool IsActive);

public sealed record DeliveryView(Guid Id, string EventType, DeliveryStatus Status, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt, DateTimeOffset NextAttemptAt, int? LastStatusCode, string? LastError, string Payload);

/// <summary>Kaydedilen adres ve yeni gizli anahtar. Anahtar yalnızca bu yanıtta açık gösterilir.</summary>
public sealed record EndpointSecret(Guid Id, string Secret);

public sealed record WebhookTestResult(bool Success, int? StatusCode, string? Error);

/// <summary>
/// Modül olaylarını abone dış sistemlere imzalı JSON olarak gönderir. Olay, adres başına bir gönderim kaydı
/// olarak kuyruğa yazılır; zamanlanmış iş gönderir ve başarısız olanları artan aralıklarla yeniden dener.
/// </summary>
public sealed class WebhookService(
    WebhooksDbContext db,
    IContactDirectory contacts,
    IDataProtectionProvider protection,
    IHttpClientFactory httpClients,
    IAuditLog audit,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<WebhookService> logger)
{
    public const string HttpClientName = "webhooks";

    /// <summary>Bir çalıştırmada gönderilen en fazla kayıt.</summary>
    internal const int BatchSize = 200;

    /// <summary>Bu süreden eski tamamlanmış gönderimler silinir.</summary>
    internal static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector = protection.CreateProtector("Arma.Webhooks.Secret");

    private bool AllowHttp => configuration.GetValue<bool>("Arma:Webhooks:AllowHttp");

    public async Task<IReadOnlyList<EndpointView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var endpoints = await db.Endpoints.AsNoTracking().OrderBy(e => e.Name).ToListAsync(cancellationToken);
        var ids = endpoints.Select(e => e.Id).ToList();
        var stats = await db.Deliveries.AsNoTracking().Where(d => ids.Contains(d.EndpointId)).GroupBy(d => d.EndpointId)
            .Select(g => new
            {
                g.Key,
                Pending = g.Count(d => d.Status == DeliveryStatus.Pending),
                Failed = g.Count(d => d.Status == DeliveryStatus.Failed),
                Last = g.Max(d => d.DeliveredAt),
            })
            .ToDictionaryAsync(g => g.Key, cancellationToken);
        return [.. endpoints.Select(e => new EndpointView(e.Id, e.Name, e.Url, e.Events, e.IsActive,
            stats.GetValueOrDefault(e.Id)?.Pending ?? 0, stats.GetValueOrDefault(e.Id)?.Failed ?? 0, stats.GetValueOrDefault(e.Id)?.Last))];
    }

    public async Task<IReadOnlyList<DeliveryView>> DeliveriesAsync(Guid endpointId, int take = 50, CancellationToken cancellationToken = default) =>
        await db.Deliveries.AsNoTracking().Where(d => d.EndpointId == endpointId).OrderByDescending(d => d.CreatedAt).Take(take)
            .Select(d => new DeliveryView(d.Id, d.EventType, d.Status, d.Attempts, d.CreatedAt, d.DeliveredAt, d.NextAttemptAt, d.LastStatusCode, d.LastError, d.Payload))
            .ToListAsync(cancellationToken);

    public async Task<Result<EndpointSecret>> CreateAsync(SaveEndpointRequest request, CancellationToken cancellationToken = default)
    {
        var secret = NewSecret();
        var created = WebhookEndpoint.Create(request.Name, request.Url, request.Events, request.IsActive, protector.Protect(secret), AllowHttp);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        db.Endpoints.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(AuditAction.Created, nameof(WebhookEndpoint), created.Value!.Id.ToString(), $"Webhook eklendi: {created.Value.Name} ({created.Value.Url})", cancellationToken);
        return new EndpointSecret(created.Value.Id, secret);
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, SaveEndpointRequest request, CancellationToken cancellationToken = default)
    {
        var endpoint = await db.Endpoints.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (endpoint is null)
        {
            return WebhookErrors.NotFound;
        }

        var updated = endpoint.Update(request.Name, request.Url, request.Events, request.IsActive, AllowHttp);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(AuditAction.Updated, nameof(WebhookEndpoint), id.ToString(), $"Webhook güncellendi: {endpoint.Name} ({endpoint.Url}), olaylar: {string.Join(", ", endpoint.Events)}", cancellationToken);
        return id;
    }

    public async Task<Result<EndpointSecret>> RotateSecretAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var endpoint = await db.Endpoints.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (endpoint is null)
        {
            return WebhookErrors.NotFound;
        }

        var secret = NewSecret();
        endpoint.RotateSecret(protector.Protect(secret));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(AuditAction.Updated, nameof(WebhookEndpoint), id.ToString(), $"Webhook gizli anahtarı yenilendi: {endpoint.Name}", cancellationToken);
        return new EndpointSecret(id, secret);
    }

    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var endpoint = await db.Endpoints.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (endpoint is null)
        {
            return WebhookErrors.NotFound;
        }

        await db.Deliveries.Where(d => d.EndpointId == id).ExecuteDeleteAsync(cancellationToken);
        db.Endpoints.Remove(endpoint);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(AuditAction.Deleted, nameof(WebhookEndpoint), id.ToString(), $"Webhook silindi: {endpoint.Name} ({endpoint.Url})", cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> RetryAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return WebhookErrors.DeliveryNotFound;
        }

        delivery.Retry(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return deliveryId;
    }

    /// <summary>Adrese "ping" olayı gönderir ve sonucu hemen döner; kuyruğa yazılmaz.</summary>
    public async Task<WebhookTestResult> TestAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var endpoint = await db.Endpoints.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (endpoint is null)
        {
            return new WebhookTestResult(false, null, WebhookErrors.NotFound.Message);
        }

        var deliveryId = Guid.CreateVersion7();
        var payload = Envelope(deliveryId, "ping", clock.GetUtcNow(), new { message = "Arma CRM webhook denemesi" });
        var (status, error) = await SendAsync(endpoint, deliveryId, "ping", payload, cancellationToken);
        return new WebhookTestResult(error is null, status, error);
    }

    /// <summary>Zaman çizelgesine düşen modül olayını abonelere kuyruklar.</summary>
    internal async Task OnTimelineAsync(RecordTimelineEntry entry, CancellationToken cancellationToken)
    {
        var type = $"{entry.SourceModule.ToLowerInvariant()}.activity";
        if (!WebhookEndpoint.EventTypes.ContainsKey(type))
        {
            return;
        }

        var endpoints = await SubscribersAsync(type, cancellationToken);
        if (endpoints.Count == 0)
        {
            return;
        }

        var people = await contacts.FindManyAsync([.. entry.ContactIds.Distinct()], cancellationToken);
        var data = new
        {
            source = new { module = entry.SourceModule, id = entry.SourceId },
            subject = entry.Subject,
            body = entry.Body,
            occurredAt = entry.OccurredAt,
            contacts = entry.ContactIds.Distinct().Select(id => people.TryGetValue(id, out var c)
                ? new { id, type = c.Type.ToString(), name = c.DisplayName, email = c.Email, phone = c.Phone }
                : new { id, type = (string)null!, name = (string)null!, email = (string?)null, phone = (string?)null }),
        };
        await EnqueueAsync(endpoints, type, data, cancellationToken);
    }

    internal async Task OnMergedAsync(ContactsMerged message, CancellationToken cancellationToken)
    {
        var endpoints = await SubscribersAsync("contacts.merged", cancellationToken);
        if (endpoints.Count > 0)
        {
            await EnqueueAsync(endpoints, "contacts.merged", new { primaryId = message.PrimaryId, duplicateId = message.DuplicateId }, cancellationToken);
        }
    }

    /// <summary>Zamanı gelen gönderimleri yapar, eski kayıtları temizler. Gönderilen sayıyı döner.</summary>
    public async Task<int> DeliverDueAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        await db.Deliveries.Where(d => d.Status != DeliveryStatus.Pending && d.CreatedAt < now - Retention).ExecuteDeleteAsync(cancellationToken);

        var due = await db.Deliveries.Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).Take(BatchSize).ToListAsync(cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        var endpointIds = due.Select(d => d.EndpointId).Distinct().ToList();
        var endpoints = await db.Endpoints.Where(e => endpointIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, cancellationToken);
        var sent = 0;
        foreach (var delivery in due)
        {
            if (!endpoints.TryGetValue(delivery.EndpointId, out var endpoint) || !endpoint.IsActive)
            {
                // Kapatılan adresin gönderimleri bekler; adres açılınca devam eder.
                continue;
            }

            var (status, error) = await SendAsync(endpoint, delivery.Id, delivery.EventType, delivery.Payload, cancellationToken);
            if (error is null)
            {
                delivery.Succeeded(status!.Value, clock.GetUtcNow());
                sent++;
            }
            else
            {
                delivery.AttemptFailed(status, error, clock.GetUtcNow());
                if (delivery.Status == DeliveryStatus.Failed)
                {
                    logger.LogWarning("Webhook gönderimi tüm denemelerden sonra başarısız: {Endpoint} {Event} {Error}", endpoint.Name, delivery.EventType, error);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        return sent;
    }

    /// <summary>
    /// İmza: <c>X-Arma-Signature: sha256=hex(HMAC_SHA256(gizli anahtar, "{X-Arma-Timestamp}.{gövde}"))</c>.
    /// Alıcı zaman damgasını da denetleyerek eski isteklerin tekrar gönderilmesini reddedebilir.
    /// </summary>
    public static string Sign(string secret, string timestamp, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        return "sha256=" + Convert.ToHexStringLower(hash);
    }

    private async Task<(int? Status, string? Error)> SendAsync(WebhookEndpoint endpoint, Guid deliveryId, string eventType, string payload, CancellationToken cancellationToken)
    {
        string secret;
        try
        {
            secret = protector.Unprotect(endpoint.ProtectedSecret);
        }
        catch (CryptographicException)
        {
            return (null, "Gizli anahtar çözülemedi; anahtarı yenileyin.");
        }

        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url)
        {
            Content = new StringContent(payload, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        request.Headers.Add("X-Arma-Event", eventType);
        request.Headers.Add("X-Arma-Delivery", deliveryId.ToString());
        request.Headers.Add("X-Arma-Timestamp", timestamp);
        request.Headers.Add("X-Arma-Signature", Sign(secret, timestamp, payload));
        try
        {
            using var response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            var code = (int)response.StatusCode;
            return response.IsSuccessStatusCode ? (code, null) : (code, $"HTTP {code} {response.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (null, ex is TaskCanceledException ? "Zaman aşımı" : ex.Message);
        }
    }

    private async Task<List<WebhookEndpoint>> SubscribersAsync(string type, CancellationToken cancellationToken) =>
        await db.Endpoints.AsNoTracking().Where(e => e.IsActive && e.Events.Contains(type)).ToListAsync(cancellationToken);

    private async Task EnqueueAsync(IReadOnlyList<WebhookEndpoint> endpoints, string type, object data, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        foreach (var endpoint in endpoints)
        {
            var id = Guid.CreateVersion7();
            db.Deliveries.Add(WebhookDelivery.Create(id, endpoint.Id, type, Envelope(id, type, now, data), now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string Envelope(Guid id, string type, DateTimeOffset createdAt, object data) =>
        JsonSerializer.Serialize(new { id, type, createdAt, data }, Json);

    private static string NewSecret() => "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class WebhookErrors
{
    public static readonly Error NotFound = new("webhooks.not_found", "Webhook bulunamadı.");
    public static readonly Error DeliveryNotFound = new("webhooks.delivery_not_found", "Gönderim bulunamadı.");
}
