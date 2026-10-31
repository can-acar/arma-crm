using Arma.SharedKernel.Security;

namespace Arma.Modules.Identity;

public static class IdentityPermissions
{
    private const string Group = "Kullanıcılar ve erişim";

    public const string ManageUsers = "users.manage";
    public const string ManageRoles = "roles.manage";
    public const string ManageApiClients = "apiclients.manage";

    public static readonly PermissionDefinition[] All =
    [
        new(ManageUsers, Group, "Kullanıcı ekleme, pasifleştirme, rol atama"),
        new(ManageRoles, Group, "Rol oluşturma ve izinlerini düzenleme"),
        new(ManageApiClients, Group, "API istemcisi oluşturma ve silme"),
    ];
}
