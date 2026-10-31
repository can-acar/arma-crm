using Arma.SharedKernel.Security;

namespace Arma.Modules.Privacy;

public static class PrivacyPermissions
{
    private const string Group = "KVKK";

    public const string Read = "privacy.read";
    public const string Manage = "privacy.manage";
    public const string Export = "privacy.export";
    public const string Erase = "privacy.erase";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Rıza kayıtlarını, başvuruları ve veri envanterini görüntüleme"),
        new(Manage, Group, "Aydınlatma metinlerini yayımlama, rıza kaydetme, başvuruları yanıtlama"),
        new(Export, Group, "Bir kişinin tüm verisini dışa aktarma"),
        new(Erase, Group, "Kişiyi kalıcı olarak anonimleştirme"),
    ];
}
