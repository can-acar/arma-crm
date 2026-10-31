using System.Net;
using System.Text;
using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Comms.Application;

/// <summary>İleti metnindeki alanların doldurulması ve e-posta gövdesinin üretilmesi.</summary>
public static class MessageText
{
    /// <summary>Metinde kullanılabilen alanlar; şablon ekranında gösterilir.</summary>
    public static readonly (string Field, string Description)[] Fields =
    [
        ("{{ad}}", "Kişinin adı (kurumda kurum adı)"),
        ("{{soyad}}", "Soyadı"),
        ("{{adsoyad}}", "Kayıtta görünen ad"),
        ("{{eposta}}", "E-posta adresi"),
    ];

    public static string Merge(string text, AudienceMember? contact) =>
        text.Replace("{{ad}}", contact?.FirstName ?? contact?.DisplayName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{{soyad}}", contact?.LastName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{{adsoyad}}", contact?.DisplayName ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{{eposta}}", contact?.Email ?? "", StringComparison.OrdinalIgnoreCase);

    /// <summary>Düz metni güvenli HTML'e çevirir: kodlar, paragraf ve satır sonlarını korur.</summary>
    public static string ToHtml(string text, string? unsubscribeUrl)
    {
        var html = new StringBuilder("<div style=\"font-family:Arial,Helvetica,sans-serif;font-size:14px;line-height:1.5;color:#111\">");
        foreach (var paragraph in text.Replace("\r\n", "\n").Split("\n\n"))
        {
            html.Append("<p>").Append(WebUtility.HtmlEncode(paragraph).Replace("\n", "<br>")).Append("</p>");
        }

        if (unsubscribeUrl is not null)
        {
            html.Append("<p style=\"font-size:12px;color:#666\">Bu iletileri almak istemiyorsanız <a href=\"")
                .Append(WebUtility.HtmlEncode(unsubscribeUrl)).Append("\">aboneliğinizi iptal edebilirsiniz</a>.</p>");
        }

        return html.Append("</div>").ToString();
    }

    public static string WithUnsubscribeText(string text, string? unsubscribeUrl) =>
        unsubscribeUrl is null ? text : $"{text}\n\n--\nAbonelikten çıkmak için: {unsubscribeUrl}";

    /// <summary>Türkiye cep telefonunu 5XXXXXXXXX biçimine getirir; değilse null.</summary>
    public static string? MobileNumber(string? phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (digits.StartsWith("90", StringComparison.Ordinal) && digits.Length == 12)
        {
            digits = digits[2..];
        }
        else if (digits.StartsWith('0') && digits.Length == 11)
        {
            digits = digits[1..];
        }

        return digits.Length == 10 && digits[0] == '5' ? digits : null;
    }
}
