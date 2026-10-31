using Arma.SharedKernel.Security;

namespace Arma.Modules.Forms;

public static class FormsPermissions
{
    private const string Group = "Formlar";

    public const string Read = "forms.read";
    public const string Manage = "forms.manage";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Formları ve gelen yanıtları görüntüleme"),
        new(Manage, Group, "Form oluşturma, düzenleme ve yayından kaldırma"),
    ];
}
