using Arma.Modules.Privacy.Domain;

namespace Arma.Modules.Privacy.Web;

internal static class PrivacyLabels
{
    public static readonly RequestType[] Types = [RequestType.Information, RequestType.Access, RequestType.Correction, RequestType.Erasure, RequestType.Objection, RequestType.Other];

    public static string Type(RequestType type) => type switch
    {
        RequestType.Information => "Verimin işlenip işlenmediğini öğrenme",
        RequestType.Access => "Verilerimin bir kopyası",
        RequestType.Correction => "Eksik veya yanlış verinin düzeltilmesi",
        RequestType.Erasure => "Verilerimin silinmesi",
        RequestType.Objection => "İşlemeye itiraz",
        _ => "Diğer",
    };

    public static string Channel(RequestChannel channel) => channel switch
    {
        RequestChannel.Web => "Web formu",
        RequestChannel.Email => "E-posta / KEP",
        RequestChannel.InPerson => "Elden",
        RequestChannel.Mail => "Posta / noter",
        _ => channel.ToString(),
    };

    public static string Status(RequestStatus status) => status switch
    {
        RequestStatus.New => "Yeni",
        RequestStatus.InProgress => "İnceleniyor",
        RequestStatus.Completed => "Yanıtlandı",
        RequestStatus.Rejected => "Reddedildi",
        _ => status.ToString(),
    };

    public static string Source(string? source) => source switch
    {
        "manual" => "Elle",
        "form" => "Web formu",
        "api" => "API",
        _ => source ?? "",
    };

    public static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}
