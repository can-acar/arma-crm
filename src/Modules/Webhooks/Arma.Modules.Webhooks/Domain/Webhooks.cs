using Arma.SharedKernel.Domain;

namespace Arma.Modules.Webhooks.Domain;

public enum DeliveryStatus
{
    Pending = 1,
    Succeeded = 2,

    /// <summary>Tüm denemeler başarısız oldu.</summary>
    Failed = 3,
}

/// <summary>Olayların gönderildiği dış sistem adresi. Gövde, gizli anahtarla HMAC-SHA256 imzalanır.</summary>
public sealed class WebhookEndpoint : Entity
{
    /// <summary>Abone olunabilen olaylar ve ekrandaki adları.</summary>
    public static readonly IReadOnlyDictionary<string, string> EventTypes = new Dictionary<string, string>
    {
        ["giving.activity"] = "Bağış kaydı, iadesi ve taahhüt",
        ["membership.activity"] = "Üyelik ve aidat",
        ["events.activity"] = "Etkinlik kaydı ve katılım",
        ["sales.activity"] = "Satış fırsatı ve teklif",
        ["forms.activity"] = "Form yanıtı",
        ["cases.activity"] = "Vaka açılışı, notu ve kapanışı",
        ["grants.activity"] = "Hibe durumu ve ödemesi",
        ["contacts.merged"] = "Mükerrer kişi birleştirme",
    };

    private WebhookEndpoint()
    {
    }

    public string Name { get; private set; } = "";

    public string Url { get; private set; } = "";

    /// <summary>Data Protection ile şifrelenmiş gizli anahtar.</summary>
    public string ProtectedSecret { get; private set; } = "";

    public List<string> Events { get; private set; } = [];

    public bool IsActive { get; private set; } = true;

    public static Result<WebhookEndpoint> Create(string name, string url, IEnumerable<string> events, bool isActive, string protectedSecret, bool allowHttp)
    {
        var endpoint = new WebhookEndpoint { ProtectedSecret = protectedSecret };
        var result = endpoint.Update(name, url, events, isActive, allowHttp);
        return result.IsSuccess ? endpoint : result.Error!;
    }

    public Result<bool> Update(string name, string url, IEnumerable<string> events, bool isActive, bool allowHttp)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
        {
            return new Error("webhooks.name", "Ad 1-100 karakter olmalı.");
        }

        url = url?.Trim() ?? "";
        if (url.Length > 500 || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return new Error("webhooks.url", "Geçerli bir http(s) adresi girin.");
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !allowHttp && !uri.IsLoopback)
        {
            return new Error("webhooks.https", "Adres https ile başlamalı; gizli bilgiler şifresiz gönderilmez.");
        }

        var list = events.Where(EventTypes.ContainsKey).Distinct().ToList();
        if (list.Count == 0)
        {
            return new Error("webhooks.events", "En az bir olay seçin.");
        }

        Name = name;
        Url = uri.ToString();
        Events = list;
        IsActive = isActive;
        return true;
    }

    public void RotateSecret(string protectedSecret) => ProtectedSecret = protectedSecret;
}

/// <summary>Bir olayın bir adrese gönderimi. Başarısız gönderim artan aralıklarla yeniden denenir.</summary>
public sealed class WebhookDelivery : Entity
{
    /// <summary>İlk denemeden sonraki bekleme süreleri; hepsi tükenince gönderim başarısız sayılır.</summary>
    public static readonly TimeSpan[] RetryDelays =
        [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), TimeSpan.FromHours(2), TimeSpan.FromHours(6), TimeSpan.FromHours(24)];

    private WebhookDelivery()
    {
    }

    public Guid EndpointId { get; private set; }

    public string EventType { get; private set; } = "";

    public string Payload { get; private set; } = "";

    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Pending;

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public int? LastStatusCode { get; private set; }

    public string? LastError { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public static WebhookDelivery Create(Guid id, Guid endpointId, string eventType, string payload, DateTimeOffset now) =>
        new() { Id = id, EndpointId = endpointId, EventType = eventType, Payload = payload, NextAttemptAt = now };

    public void Succeeded(int statusCode, DateTimeOffset now)
    {
        Attempts++;
        Status = DeliveryStatus.Succeeded;
        LastStatusCode = statusCode;
        LastError = null;
        DeliveredAt = now;
    }

    public void AttemptFailed(int? statusCode, string error, DateTimeOffset now)
    {
        Attempts++;
        LastStatusCode = statusCode;
        LastError = error.Length > 500 ? error[..500] : error;
        if (Attempts > RetryDelays.Length)
        {
            Status = DeliveryStatus.Failed;
            return;
        }

        NextAttemptAt = now + RetryDelays[Attempts - 1];
    }

    /// <summary>Elle yeniden deneme: deneme sayacı sıfırlanır.</summary>
    public void Retry(DateTimeOffset now)
    {
        Status = DeliveryStatus.Pending;
        Attempts = 0;
        NextAttemptAt = now;
    }
}
