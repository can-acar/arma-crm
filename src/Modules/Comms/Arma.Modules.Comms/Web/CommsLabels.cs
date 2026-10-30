using Arma.Modules.Comms.Domain;
using Arma.UI;

namespace Arma.Modules.Comms.Web;

internal static class CommsLabels
{
    public static string Channel(Channel channel) => channel switch
    {
        Domain.Channel.Email => "E-posta",
        Domain.Channel.Sms => "SMS",
        Domain.Channel.Call => "Arama",
        _ => channel.ToString(),
    };

    public static string Consent(ConsentStatus status) => status switch
    {
        ConsentStatus.Approved => "Onaylı",
        ConsentStatus.Rejected => "Ret",
        _ => "Bilinmiyor",
    };

    public static BadgeVariant ConsentVariant(ConsentStatus status) => status switch
    {
        ConsentStatus.Approved => BadgeVariant.Default,
        ConsentStatus.Rejected => BadgeVariant.Destructive,
        _ => BadgeVariant.Outline,
    };

    public static string Source(string? source) => source switch
    {
        "form" => "Web formu",
        "manual" => "Elle",
        "import" => "İçe aktarma",
        "unsubscribe" => "Abonelik iptali",
        "api" => "API",
        null or "" => "",
        _ => source,
    };

    public static string Iys(IysState state) => state switch
    {
        IysState.Pending => "İYS'ye bildiriliyor",
        IysState.Synced => "İYS'de kayıtlı",
        IysState.Failed => "İYS bildirimi başarısız",
        _ => "",
    };

    public static string Status(MessageStatus status) => status switch
    {
        MessageStatus.Pending => "Kuyrukta",
        MessageStatus.Sent => "Gönderildi",
        MessageStatus.Failed => "Başarısız",
        MessageStatus.Skipped => "Gönderilmedi",
        _ => status.ToString(),
    };

    public static BadgeVariant StatusVariant(MessageStatus status) => status switch
    {
        MessageStatus.Sent => BadgeVariant.Secondary,
        MessageStatus.Failed => BadgeVariant.Destructive,
        _ => BadgeVariant.Outline,
    };

    public static string Mailing(MailingStatus status) => status switch
    {
        MailingStatus.Draft => "Taslak",
        MailingStatus.Sending => "Gönderiliyor",
        MailingStatus.Completed => "Tamamlandı",
        _ => status.ToString(),
    };

    public static string DateTime(DateTimeOffset value) => value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
