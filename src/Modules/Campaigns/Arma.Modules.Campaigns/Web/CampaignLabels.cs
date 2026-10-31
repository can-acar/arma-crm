using System.Globalization;
using Arma.Modules.Campaigns.Domain;
using Arma.SharedKernel.Modules;

namespace Arma.Modules.Campaigns.Web;

public static class CampaignLabels
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Status(CampaignStatus status) => status switch
    {
        CampaignStatus.Planned => "Planlandı",
        CampaignStatus.Active => "Sürüyor",
        CampaignStatus.Completed => "Tamamlandı",
        CampaignStatus.Cancelled => "İptal edildi",
        _ => status.ToString(),
    };

    public static string Money(decimal amount) => string.Format(Turkish, "{0:N2} TL", amount);

    public static string Value(CampaignMetric metric) => metric.Format == "money" ? Money(metric.Value) : metric.Value.ToString("N0", Turkish);

    public static string Period(DateOnly? from, DateOnly? to) => (from, to) switch
    {
        (null, null) => "Tarih yok",
        ({ } f, null) => $"{f.ToString("dd.MM.yyyy", Turkish)} –",
        (null, { } t) => $"– {t.ToString("dd.MM.yyyy", Turkish)}",
        ({ } f, { } t) => $"{f.ToString("dd.MM.yyyy", Turkish)} – {t.ToString("dd.MM.yyyy", Turkish)}",
    };

    public static int Percent(decimal raised, decimal? goal) => goal is > 0 ? (int)Math.Min(100, Math.Round(raised / goal.Value * 100)) : 0;

    public static bool TryParseMoney(string? text, out decimal value) =>
        decimal.TryParse(text?.Replace("TL", "").Trim(), NumberStyles.Number, Turkish, out value);
}
