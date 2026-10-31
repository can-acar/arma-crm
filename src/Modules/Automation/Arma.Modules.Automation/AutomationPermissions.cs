using Arma.SharedKernel.Security;

namespace Arma.Modules.Automation;

public static class AutomationPermissions
{
    private const string Group = "Otomasyon";

    public const string Read = "automation.read";
    public const string Manage = "automation.manage";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Otomasyon kurallarını ve çalışma geçmişini görüntüleme"),
        new(Manage, Group, "Otomasyon kuralı oluşturma, açma ve kapatma"),
    ];
}
