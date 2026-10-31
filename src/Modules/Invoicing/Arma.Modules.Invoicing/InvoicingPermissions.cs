using Arma.SharedKernel.Security;

namespace Arma.Modules.Invoicing;

public static class InvoicingPermissions
{
    private const string Group = "Faturalar";

    public const string Read = "invoicing.read";
    public const string Write = "invoicing.write";
    public const string Settings = "invoicing.settings";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Faturaları ve fatura dosyalarını görüntüleme"),
        new(Write, Group, "Taslak faturayı düzenleme, gönderme ve iptal etme"),
        new(Settings, Group, "Kurum fatura bilgileri ve e-Fatura entegratörü ayarları"),
    ];
}
