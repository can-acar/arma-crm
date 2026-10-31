using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arma.Modules.Comms.Senders;

public sealed record IysConsent(string Recipient, bool IsEmail, bool Approved, bool IsCall, DateTimeOffset At, string Source);

/// <summary>İleti Yönetim Sistemi'ne izin bildirimi (hizmet sağlayıcı API'si).</summary>
public interface IIysClient
{
    bool Enabled { get; }

    Task<SendResult> PushAsync(IysConsent consent, CancellationToken cancellationToken);
}

internal sealed class IysClient(HttpClient http, IOptions<CommsOptions> options, ILogger<IysClient> logger) : IIysClient
{
    private IysOptions Settings => options.Value.Iys;

    public bool Enabled => Settings.Enabled;

    public async Task<SendResult> PushAsync(IysConsent consent, CancellationToken cancellationToken)
    {
        try
        {
            var token = await TokenAsync(cancellationToken);
            if (token is null)
            {
                return SendResult.Fail("İYS'ye giriş yapılamadı; kullanıcı bilgilerini kontrol edin.");
            }

            var body = new
            {
                consentDate = consent.At.ToOffset(TimeSpan.FromHours(3)).ToString("yyyy-MM-dd HH:mm:ss"),
                source = consent.Source,
                recipient = consent.Recipient,
                recipientType = "BIREYSEL",
                status = consent.Approved ? "ONAY" : "RET",
                type = consent.IsCall ? "ARAMA" : consent.IsEmail ? "EPOSTA" : "MESAJ",
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Settings.BaseUrl.TrimEnd('/')}/sps/{Settings.IysCode}/brands/{Settings.BrandCode}/consents")
            {
                Content = JsonContent.Create(body),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return SendResult.Ok(await ReadIdAsync(response, cancellationToken));
            }

            return SendResult.Fail($"İYS yanıtı {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(ex, "İYS'ye bağlanılamadı");
            return SendResult.Fail("İYS'ye bağlanılamadı.");
        }
    }

    private async Task<string?> TokenAsync(CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync($"{Settings.BaseUrl.TrimEnd('/')}/oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = Settings.UserName,
            ["password"] = Settings.Password,
        }), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return json.RootElement.TryGetProperty("access_token", out var t) ? t.GetString() : null;
    }

    private static async Task<string?> ReadIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        using var json = JsonDocument.Parse(text);
        return json.RootElement.TryGetProperty("requestId", out var id) ? id.ToString() : null;
    }
}

internal sealed class DisabledIysClient : IIysClient
{
    public bool Enabled => false;

    public Task<SendResult> PushAsync(IysConsent consent, CancellationToken cancellationToken) =>
        Task.FromResult(SendResult.Fail("İYS entegrasyonu kapalı."));
}
