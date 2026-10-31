using Arma.SharedKernel.Security;

namespace Arma.Modules.Comms;

public static class CommsPermissions
{
    private const string Group = "İletişim";

    public const string Read = "comms.read";
    public const string Send = "comms.send";
    public const string Mailings = "comms.mailings";
    public const string Consent = "comms.consent";
    public const string Settings = "comms.settings";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Gönderilen iletileri ve ileti izinlerini görüntüleme"),
        new(Send, Group, "Bir kişiye e-posta veya SMS gönderme"),
        new(Mailings, Group, "Toplu e-posta ve SMS hazırlama, gönderme; şablonları yönetme"),
        new(Consent, Group, "Ticari ileti izinlerini (İYS) değiştirme"),
        new(Settings, Group, "E-posta, SMS ve İYS bağlantısını denetleme, deneme gönderimi"),
    ];
}
