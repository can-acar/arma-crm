using System.Globalization;
using Arma.Modules.Membership.Domain;

namespace Arma.Modules.Membership.Web;

public static class MembershipLabels
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public static readonly MembershipStatus[] Statuses = Enum.GetValues<MembershipStatus>();

    public static readonly DuesMethod[] Methods = Enum.GetValues<DuesMethod>();

    public static string Status(MembershipStatus status) => status switch
    {
        MembershipStatus.Pending => "Başlamadı",
        MembershipStatus.Current => "Aktif",
        MembershipStatus.Grace => "Aidat bekliyor",
        MembershipStatus.Expired => "Süresi doldu",
        MembershipStatus.Cancelled => "Ayrıldı",
        _ => status.ToString(),
    };

    public static string Method(DuesMethod method) => method switch
    {
        DuesMethod.Cash => "Nakit",
        DuesMethod.BankTransfer => "Havale/EFT",
        DuesMethod.Card => "Kredi kartı",
        _ => "Diğer",
    };

    public static string Duration(int months) => months switch
    {
        0 => "Ömür boyu",
        12 => "Yıllık",
        1 => "Aylık",
        3 => "3 aylık",
        6 => "6 aylık",
        _ when months % 12 == 0 => $"{months / 12} yıllık",
        _ => $"{months} aylık",
    };

    public static string Date(DateOnly date) => date.ToString("dd.MM.yyyy", Turkish);

    public static string Money(decimal amount) => string.Format(Turkish, "{0:N2} TL", amount);

    public static bool TryParseMoney(string? text, out decimal amount) =>
        decimal.TryParse(text?.Replace("TL", "").Trim(), NumberStyles.Number, Turkish, out amount);
}
