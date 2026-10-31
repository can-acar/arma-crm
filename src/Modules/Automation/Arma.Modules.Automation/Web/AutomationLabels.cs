using Arma.Modules.Automation.Domain;

namespace Arma.Modules.Automation.Web;

public static class AutomationLabels
{
    public static string Source(string? module) => module switch
    {
        "Giving" => "Bağışlar",
        "Membership" => "Üyelik",
        "Events" => "Etkinlikler",
        "Sales" => "Satış",
        "Forms" => "Formlar",
        _ => module ?? "",
    };

    /// <summary>Kaynak modülün zaman akışına yazdığı konulardan örnekler; kural ekranında ipucu olarak gösterilir.</summary>
    public static string SubjectHint(string? module) => module switch
    {
        "Giving" => "Örnek: \"Bağış:\" (her bağış)",
        "Membership" => "Örnek: \"Üye oldu\", \"Üyelik yenilendi\", \"Aidat alındı\"",
        "Events" => "Örnek: \"Etkinliğe kayıt\", \"Etkinliğe katıldı\"",
        "Sales" => "Örnek: \"Satış kazanıldı\", \"Satış kaybedildi\", \"Teklif kabul edildi\"",
        "Forms" => "Örnek: \"Form dolduruldu: Gönüllü başvurusu\"",
        _ => "",
    };

    public static string Action(ActionKind kind) => kind switch
    {
        ActionKind.AddTag => "Etiket ekle",
        ActionKind.AddToGroup => "Gruba ekle",
        ActionKind.CreateTask => "Görev aç",
        ActionKind.SendEmail => "E-posta gönder",
        _ => kind.ToString(),
    };

    public static string Status(RunStatus status) => status switch
    {
        RunStatus.Done => "Uygulandı",
        RunStatus.Skipped => "Kural açılırken zaten uyuyordu",
        _ => status.ToString(),
    };

    public static string Describe(RuleAction action) => action.Kind switch
    {
        ActionKind.AddTag => $"\"{action.TagName}\" etiketi eklenir",
        ActionKind.AddToGroup => "Gruba eklenir",
        ActionKind.CreateTask => $"\"{action.Subject}\" görevi açılır ({(action.DueDays is > 0 ? $"{action.DueDays} gün içinde" : "aynı gün")})",
        ActionKind.SendEmail => $"\"{action.Subject}\" e-postası gider",
        _ => "",
    };
}
