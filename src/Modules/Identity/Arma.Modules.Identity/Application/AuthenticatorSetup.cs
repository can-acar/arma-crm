using System.Text;
using QRCoder;

namespace Arma.Modules.Identity.Application;

/// <summary>Kimlik doğrulayıcı uygulamaları (Google Authenticator, Microsoft Authenticator vb.) için eşleştirme bilgisi.</summary>
public static class AuthenticatorSetup
{
    private const string Issuer = "Arma CRM";

    /// <summary>otpauth:// adresi; QR kodun içeriği budur.</summary>
    public static string BuildUri(string email, string key) =>
        $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}?secret={key}&issuer={Uri.EscapeDataString(Issuer)}&digits=6";

    /// <summary>Elle girişte okunması kolay olsun diye anahtarı dörtlü gruplara böler.</summary>
    public static string FormatKey(string key)
    {
        var result = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                result.Append(' ');
            }

            result.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return result.ToString().ToLowerInvariant();
    }

    /// <summary>Sunucuda üretilen SVG; sayfaya JavaScript gerekmeden gömülür.</summary>
    public static string RenderQrSvg(string uri)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        return new SvgQRCode(data).GetGraphic(4);
    }
}
