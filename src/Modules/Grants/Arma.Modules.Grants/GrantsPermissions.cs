using Arma.SharedKernel.Security;

namespace Arma.Modules.Grants;

public static class GrantsPermissions
{
    private const string Group = "Hibeler";

    public const string Read = "grants.read";
    public const string Write = "grants.write";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Hibe başvurularını, takvimini ve tutarlarını görüntüleme"),
        new(Write, Group, "Hibe başvurusu ekleme, durum değiştirme, teslim ve ödeme takibi"),
    ];
}
