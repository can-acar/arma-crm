using Arma.Modules.Cases.Domain;
using Arma.UI;

namespace Arma.Modules.Cases.Web;

public static class CaseLabels
{
    public static string Status(CaseStatus status) => status switch
    {
        CaseStatus.Open => "Açık",
        CaseStatus.OnHold => "Beklemede",
        _ => "Kapalı",
    };

    public static string Priority(CasePriority priority) => priority switch
    {
        CasePriority.Low => "Düşük",
        CasePriority.High => "Yüksek",
        CasePriority.Urgent => "Acil",
        _ => "Normal",
    };

    public static BadgeVariant StatusVariant(CaseStatus status) => status switch
    {
        CaseStatus.Open => BadgeVariant.Default,
        CaseStatus.OnHold => BadgeVariant.Secondary,
        _ => BadgeVariant.Outline,
    };

    public static string PriorityClass(CasePriority priority) => priority switch
    {
        CasePriority.Urgent => "text-destructive font-medium",
        CasePriority.High => "text-amber-600 dark:text-amber-500 font-medium",
        _ => "text-muted-foreground",
    };
}
