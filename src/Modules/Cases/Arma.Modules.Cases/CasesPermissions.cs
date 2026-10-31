using Arma.SharedKernel.Security;

namespace Arma.Modules.Cases;

public static class CasesPermissions
{
    private const string Group = "Vakalar";

    public const string Read = "cases.read";
    public const string Write = "cases.write";
    public const string Confidential = "cases.confidential";
    public const string Settings = "cases.settings";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Vakaları ve adımlarını görüntüleme"),
        new(Write, Group, "Vaka açma, atama, adım ve not ekleme, kapatma"),
        new(Confidential, Group, "Gizli vaka notlarını görme ve yazma"),
        new(Settings, Group, "Vaka türlerini ve adım şablonlarını yönetme"),
    ];
}
