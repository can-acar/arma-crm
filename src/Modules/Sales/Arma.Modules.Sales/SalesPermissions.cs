using Arma.SharedKernel.Security;

namespace Arma.Modules.Sales;

public static class SalesPermissions
{
    private const string Group = "Satış";

    public const string Read = "sales.read";
    public const string Write = "sales.write";
    public const string Settings = "sales.settings";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Satış hattını, fırsatları ve teklifleri görüntüleme"),
        new(Write, Group, "Fırsat ve teklif oluşturma, aşama değiştirme"),
        new(Settings, Group, "Satış hatlarını ve aşamalarını yönetme"),
    ];
}
