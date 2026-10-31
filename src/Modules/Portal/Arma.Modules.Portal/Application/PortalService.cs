using System.Security.Cryptography;
using Arma.Modules.Comms.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Portal;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wolverine;

namespace Arma.Modules.Portal.Application;

/// <summary>
/// Bağışçı portalına parolasız giriş: kişi e-postasını yazar, kayıtlı adresine 30 dakika geçerli, tek kullanımlık
/// bir bağlantı gider. Bağlantı onaylanınca 7 gün geçerli, şifreli bir çerez yazılır.
/// </summary>
public sealed class PortalService(
    IContactDirectory contacts,
    IMessageBus bus,
    IDataProtectionProvider protection,
    IHttpContextAccessor http,
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<PortalService> logger) : IPortalSession
{
    public const string CookieName = "arma.portal";
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    /// <summary>Aynı adrese bu süre içinde ikinci bağlantı gönderilmez.</summary>
    private static readonly TimeSpan Throttle = TimeSpan.FromMinutes(1);

    private readonly ITimeLimitedDataProtector links = protection.CreateProtector("Arma.Portal.Link").ToTimeLimitedDataProtector();
    private readonly ITimeLimitedDataProtector sessions = protection.CreateProtector("Arma.Portal.Session").ToTimeLimitedDataProtector();
    private Task<Guid?>? session;

    private string? PublicUrl => configuration["Comms:PublicUrl"]?.TrimEnd('/') is { Length: > 0 } url ? url : null;

    /// <summary>
    /// Adres kayıtlıysa giriş bağlantısı gönderir. Adresin kayıtlı olup olmadığı dışarıya belli edilmez; sonuç
    /// yalnızca testler ve günlük için döner.
    /// </summary>
    public async Task<bool> RequestLinkAsync(string? email, CancellationToken cancellationToken = default)
    {
        email = email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || email.Length > 254 || !email.Contains('@'))
        {
            return false;
        }

        if (cache.TryGetValue($"portal:throttle:{email}", out _))
        {
            return false;
        }

        cache.Set($"portal:throttle:{email}", true, Throttle);
        var contact = await contacts.FindByEmailAsync(email, cancellationToken);
        if (contact is null)
        {
            return false;
        }

        if (PublicUrl is not { } baseUrl)
        {
            logger.LogWarning("Portal giriş bağlantısı gönderilemedi: Comms:PublicUrl ayarlı değil.");
            return false;
        }

        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var token = links.Protect($"{contact.Id:N}|{nonce}", LinkLifetime);
        var link = $"{baseUrl}/portal/giris?t={Uri.EscapeDataString(token)}";
        await bus.PublishAsync(new SendTransactionalEmail(contact.Id, "Bağışçı portalı giriş bağlantınız",
            $"""
            Merhaba {contact.DisplayName},

            Bağış geçmişinizi, makbuzlarınızı ve iletişim tercihlerinizi görmek için aşağıdaki bağlantıyı açın:

            {link}

            Bağlantı {LinkLifetime.TotalMinutes:0} dakika geçerlidir ve bir kez kullanılabilir. Bu isteği siz yapmadıysanız iletiyi yok sayabilirsiniz.
            """));
        return true;
    }

    /// <summary>Bağlantıdaki belirtecin kişisi; süresi dolmuş, bozuk veya kullanılmış belirteçte null.</summary>
    public Guid? ReadLink(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            var parts = links.Unprotect(token).Split('|');
            return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var id) && !cache.TryGetValue($"portal:used:{parts[1]}", out _) ? id : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Belirteci kullanılmış sayar ve portal çerezini yazar. Statik (sunucu tarafı) sayfadan çağrılmalıdır.</summary>
    public async Task<bool> SignInAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (ReadLink(token) is not { } id || http.HttpContext is not { } context || await contacts.FindAsync(id, cancellationToken) is not { } contact)
        {
            return false;
        }

        cache.Set($"portal:used:{links.Unprotect(token!).Split('|')[1]}", true, LinkLifetime);
        context.Response.Cookies.Append(CookieName, sessions.Protect(contact.Id.ToString("N"), SessionLifetime), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            Path = "/",
            MaxAge = SessionLifetime,
        });
        session = Task.FromResult<Guid?>(contact.Id);
        return true;
    }

    public void SignOut()
    {
        http.HttpContext?.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/" });
        session = Task.FromResult<Guid?>(null);
    }

    /// <summary>
    /// Oturumdaki kişi. Düzen ve sayfa aynı istekte birlikte sorar; çözüm bir kez yapılır ve paylaşılır.
    /// </summary>
    public Task<Guid?> ContactIdAsync(CancellationToken cancellationToken = default) => session ??= ResolveAsync(cancellationToken);

    private async Task<Guid?> ResolveAsync(CancellationToken cancellationToken)
    {
        if (http.HttpContext?.Request.Cookies[CookieName] is not { Length: > 0 } cookie)
        {
            return null;
        }

        try
        {
            if (Guid.TryParseExact(sessions.Unprotect(cookie), "N", out var id))
            {
                // Kişi birleştirildiyse asıl kayda, silindiyse oturumsuz sayılır.
                return (await contacts.FindAsync(id, cancellationToken))?.Id;
            }
        }
        catch (CryptographicException)
        {
        }

        return null;
    }
}
