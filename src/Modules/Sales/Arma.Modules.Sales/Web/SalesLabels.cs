using System.Globalization;
using Arma.Modules.Sales.Domain;

namespace Arma.Modules.Sales.Web;

public static class SalesLabels
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Status(QuoteStatus status) => status switch
    {
        QuoteStatus.Draft => "Taslak",
        QuoteStatus.Sent => "Gönderildi",
        QuoteStatus.Accepted => "Kabul edildi",
        QuoteStatus.Rejected => "Reddedildi",
        _ => status.ToString(),
    };

    public static string Kind(StageKind kind) => kind switch
    {
        StageKind.Open => "Açık",
        StageKind.Won => "Kazanıldı",
        StageKind.Lost => "Kaybedildi",
        _ => kind.ToString(),
    };

    public static string Money(decimal amount) => string.Format(Turkish, "{0:N2} TL", amount);

    public static string Number(decimal value) => value.ToString("0.###", Turkish);

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", Turkish);

    public static bool TryParse(string? text, out decimal value) =>
        decimal.TryParse(text?.Replace("TL", "").Replace("%", "").Trim(), NumberStyles.Number, Turkish, out value);
}
