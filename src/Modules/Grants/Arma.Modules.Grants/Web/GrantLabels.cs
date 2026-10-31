using Arma.Modules.Grants.Domain;
using Arma.UI;

namespace Arma.Modules.Grants.Web;

public static class GrantLabels
{
    public static readonly string[] Currencies = ["TRY", "USD", "EUR", "GBP"];

    public static string Status(GrantStatus status) => status switch
    {
        GrantStatus.Preparing => "Hazırlanıyor",
        GrantStatus.Submitted => "Başvuruldu",
        GrantStatus.Awarded => "Kabul edildi",
        GrantStatus.Rejected => "Reddedildi",
        _ => "Tamamlandı",
    };

    public static BadgeVariant StatusVariant(GrantStatus status) => status switch
    {
        GrantStatus.Awarded => BadgeVariant.Default,
        GrantStatus.Rejected => BadgeVariant.Destructive,
        GrantStatus.Closed => BadgeVariant.Secondary,
        _ => BadgeVariant.Outline,
    };

    public static string Kind(MilestoneKind kind) => kind switch
    {
        MilestoneKind.Report => "Rapor / teslim",
        MilestoneKind.Payment => "Ödeme dilimi",
        _ => "Diğer",
    };

    public static string Date(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? "-";

    public static string Money(decimal amount, string currency) => Application.GrantService.Money(amount, currency);
}
