using Arma.SharedKernel.Security;

namespace Arma.Modules.Giving;

public static class GivingPermissions
{
    private const string Group = "Bağışlar";

    public const string Read = "giving.read";
    public const string Write = "giving.write";
    public const string Delete = "giving.delete";
    public const string Receipts = "giving.receipts";
    public const string Settings = "giving.settings";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Bağışları ve bağışçı toplamlarını görüntüleme"),
        new(Write, Group, "Bağış kaydetme, düzenleme ve iade"),
        new(Delete, Group, "Makbuzu kesilmemiş bağışı silme"),
        new(Receipts, Group, "Makbuz kesme ve iptal etme"),
        new(Settings, Group, "Fonları ve makbuz bilgilerini yönetme"),
    ];
}
