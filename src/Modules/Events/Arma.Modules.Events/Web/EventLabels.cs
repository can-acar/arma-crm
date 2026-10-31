using System.Globalization;
using Arma.Modules.Events.Domain;

namespace Arma.Modules.Events.Web;

public static class EventLabels
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static string Status(RegistrationStatus status) => status switch
    {
        RegistrationStatus.Registered => "Kayıtlı",
        RegistrationStatus.Waitlisted => "Yedek",
        RegistrationStatus.Cancelled => "İptal",
        _ => status.ToString(),
    };

    public static string Payment(PaymentState payment) => payment switch
    {
        PaymentState.Free => "Ücretsiz",
        PaymentState.Unpaid => "Ödenmedi",
        PaymentState.Paid => "Ödendi",
        _ => payment.ToString(),
    };

    public static string EventStatus(Domain.EventStatus status) => status switch
    {
        Domain.EventStatus.Draft => "Taslak",
        Domain.EventStatus.Published => "Yayında",
        Domain.EventStatus.Cancelled => "İptal edildi",
        _ => status.ToString(),
    };

    public static string When(DateTimeOffset start, DateTimeOffset end)
    {
        var (s, e) = (start.ToLocalTime(), end.ToLocalTime());
        return s.Date == e.Date
            ? $"{s.ToString("d MMMM yyyy ddd, HH:mm", Turkish)}–{e:HH:mm}"
            : $"{s.ToString("d MMMM yyyy HH:mm", Turkish)} – {e.ToString("d MMMM yyyy HH:mm", Turkish)}";
    }

    public static string Money(decimal amount) => amount <= 0 ? "Ücretsiz" : string.Format(Turkish, "{0:N2} TL", amount);

    public static bool TryParseMoney(string? text, out decimal amount)
    {
        amount = 0;
        return string.IsNullOrWhiteSpace(text) || decimal.TryParse(text.Replace("TL", "").Trim(), NumberStyles.Number, Turkish, out amount);
    }
}
