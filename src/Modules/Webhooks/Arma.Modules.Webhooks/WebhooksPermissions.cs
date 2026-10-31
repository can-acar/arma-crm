using Arma.SharedKernel.Security;

namespace Arma.Modules.Webhooks;

public static class WebhooksPermissions
{
    public const string Manage = "webhooks.manage";

    public static readonly PermissionDefinition[] All =
    [
        new(Manage, "Entegrasyon", "Webhook adresi ekleme, gizli anahtarı görme ve gönderimleri izleme"),
    ];
}
