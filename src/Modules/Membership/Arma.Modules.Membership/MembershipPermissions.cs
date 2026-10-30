using Arma.SharedKernel.Security;

namespace Arma.Modules.Membership;

public static class MembershipPermissions
{
    private const string Group = "Üyelik";

    public const string Read = "membership.read";
    public const string Write = "membership.write";
    public const string Settings = "membership.settings";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Üyeleri, aidat ödemelerini ve üye kartını görüntüleme"),
        new(Write, Group, "Üye kaydetme, aidat alma, yenileme ve üyeliği sonlandırma"),
        new(Settings, Group, "Üyelik türlerini ve aidat tutarlarını yönetme"),
    ];
}
