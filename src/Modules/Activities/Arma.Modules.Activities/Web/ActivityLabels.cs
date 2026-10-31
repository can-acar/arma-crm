using Arma.Modules.Activities.Contracts;
using Arma.Modules.Activities.Domain;

namespace Arma.Modules.Activities.Web;

public static class ActivityLabels
{
    /// <summary>Elle eklenebilen türler, formdaki sırasıyla.</summary>
    public static readonly ActivityKind[] UserKinds =
        [ActivityKind.Note, ActivityKind.Call, ActivityKind.Meeting, ActivityKind.Task, ActivityKind.Email, ActivityKind.Sms];

    public static string Kind(ActivityKind kind) => kind switch
    {
        ActivityKind.Note => "Not",
        ActivityKind.Call => "Arama",
        ActivityKind.Meeting => "Toplantı",
        ActivityKind.Task => "Görev",
        ActivityKind.Email => "E-posta",
        ActivityKind.Sms => "SMS",
        _ => "Sistem",
    };

    public static string Status(ActivityStatus status) => status switch
    {
        ActivityStatus.Open => "Açık",
        ActivityStatus.Done => "Tamamlandı",
        _ => "İptal",
    };

    /// <summary>Türün zaman çizelgesindeki rengi.</summary>
    public static string KindClass(ActivityKind kind) => kind switch
    {
        ActivityKind.Call => "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
        ActivityKind.Meeting => "bg-purple-100 text-purple-800 dark:bg-purple-950 dark:text-purple-200",
        ActivityKind.Task => "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
        ActivityKind.Email or ActivityKind.Sms => "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200",
        _ => "bg-secondary text-secondary-foreground",
    };

    public static string Date(DateTimeOffset? value) => value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "";

    /// <summary>Formdaki datetime-local değeri sunucu saat dilimindedir; UTC'ye çevrilir.</summary>
    public static DateTimeOffset? FromLocal(DateTime? value)
    {
        if (value is not { } local)
        {
            return null;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }
}
