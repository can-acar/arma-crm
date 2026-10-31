using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Arma.Modules.Comms.Senders;

public sealed record EmailEnvelope(string To, string? ToName, string Subject, string TextBody, string HtmlBody, string? UnsubscribeUrl);

/// <summary>Gönderim sonucu; başarısızsa <see cref="Error"/> doludur.</summary>
public sealed record SendResult(bool Success, string? ProviderId, string? Error)
{
    public static SendResult Ok(string? id = null) => new(true, id, null);

    public static SendResult Fail(string error) => new(false, null, error);
}

public interface IEmailSender
{
    Task<SendResult> SendAsync(EmailEnvelope email, CancellationToken cancellationToken);
}

public interface ISmsSender
{
    /// <param name="commercial">Ticari iletide servis, İYS'de izni olmayan numaraları kendisi de eler.</param>
    Task<SendResult> SendAsync(string phone, string text, bool commercial, CancellationToken cancellationToken);
}

internal sealed class SmtpEmailSender(IOptions<CommsOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task<SendResult> SendAsync(EmailEnvelope email, CancellationToken cancellationToken)
    {
        var settings = options.Value.Email;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName ?? settings.FromAddress, settings.FromAddress));
        message.To.Add(new MailboxAddress(email.ToName ?? email.To, email.To));
        message.Subject = email.Subject;
        if (email.UnsubscribeUrl is not null)
        {
            message.Headers.Add("List-Unsubscribe", $"<{email.UnsubscribeUrl}>");
            message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        message.Body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody }.ToMessageBody();

        try
        {
            using var client = new SmtpClient();
            var security = settings.Security.ToLowerInvariant() switch
            {
                "ssl" => SecureSocketOptions.SslOnConnect,
                "starttls" => SecureSocketOptions.StartTls,
                "none" => SecureSocketOptions.None,
                _ => SecureSocketOptions.Auto,
            };
            await client.ConnectAsync(settings.Host, settings.Port, security, cancellationToken);
            if (!string.IsNullOrEmpty(settings.UserName))
            {
                await client.AuthenticateAsync(settings.UserName, settings.Password ?? "", cancellationToken);
            }

            var response = await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return SendResult.Ok(message.MessageId ?? response);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "E-posta gönderilemedi: {To}", email.To);
            return SendResult.Fail(ex.Message);
        }
    }
}

/// <summary>
/// Netgsm SMS API. Ticari iletide "iysfilter=11" ile gönderilir; Netgsm İYS'de izni olmayan numaraya iletmez.
/// </summary>
internal sealed class NetgsmSmsSender(HttpClient http, IOptions<CommsOptions> options, ILogger<NetgsmSmsSender> logger) : ISmsSender
{
    private const string SendUrl = "https://api.netgsm.com.tr/sms/send/get";

    public async Task<SendResult> SendAsync(string phone, string text, bool commercial, CancellationToken cancellationToken)
    {
        var settings = options.Value.Sms;
        var form = new Dictionary<string, string>
        {
            ["usercode"] = settings.UserCode,
            ["password"] = settings.Password,
            ["gsmno"] = phone,
            ["message"] = text,
            ["msgheader"] = settings.Header,
            ["dil"] = "TR",
            ["iysfilter"] = commercial ? "11" : "0",
        };

        try
        {
            using var response = await http.PostAsync(SendUrl, new FormUrlEncodedContent(form), cancellationToken);
            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();

            // Başarılı yanıt "00 <iş numarası>" (veya 01, 02) biçimindedir; diğer kodlar hatadır.
            var parts = body.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && parts[0] is "00" or "01" or "02")
            {
                return SendResult.Ok(parts.Length > 1 ? parts[1] : null);
            }

            var error = parts.FirstOrDefault() switch
            {
                "20" => "Mesaj metni hatalı veya çok uzun",
                "30" => "Netgsm kullanıcı bilgileri hatalı veya API erişimi kapalı",
                "40" => "Gönderici adı (mesaj başlığı) tanımlı değil",
                "50" or "51" => "İYS kontrollü gönderim hesabında tanımlı değil",
                "70" => "Eksik veya hatalı parametre",
                "85" => "Aynı numaraya kısa sürede çok fazla gönderim",
                _ => $"Netgsm yanıtı: {body}",
            };
            return SendResult.Fail(error);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Netgsm'e bağlanılamadı");
            return SendResult.Fail("SMS servisine bağlanılamadı.");
        }
    }
}

/// <summary>Geliştirme ve testte iletileri yalnızca bellekte tutar; gerçek gönderim yapmaz.</summary>
public sealed class TestMessageSink : IEmailSender, ISmsSender
{
    private readonly ConcurrentQueue<(string To, string? Subject, string Body)> sent = new();

    public IReadOnlyCollection<(string To, string? Subject, string Body)> Sent => sent.ToArray();

    public Task<SendResult> SendAsync(EmailEnvelope email, CancellationToken cancellationToken)
    {
        sent.Enqueue((email.To, email.Subject, email.TextBody));
        return Task.FromResult(SendResult.Ok("test-" + sent.Count.ToString(CultureInfo.InvariantCulture)));
    }

    public Task<SendResult> SendAsync(string phone, string text, bool commercial, CancellationToken cancellationToken)
    {
        sent.Enqueue((phone, null, text));
        return Task.FromResult(SendResult.Ok("test-" + sent.Count.ToString(CultureInfo.InvariantCulture)));
    }
}

/// <summary>Gönderim kapalıyken kullanılır; ileti "başarısız" olarak işaretlenir.</summary>
internal sealed class DisabledSender : IEmailSender, ISmsSender
{
    public Task<SendResult> SendAsync(EmailEnvelope email, CancellationToken cancellationToken) =>
        Task.FromResult(SendResult.Fail("E-posta gönderimi yapılandırılmamış."));

    public Task<SendResult> SendAsync(string phone, string text, bool commercial, CancellationToken cancellationToken) =>
        Task.FromResult(SendResult.Fail("SMS gönderimi yapılandırılmamış."));
}
