using Arma.SharedKernel.Security;

namespace Arma.Modules.Campaigns;

public static class CampaignsPermissions
{
    private const string Group = "Kampanyalar";

    public const string Read = "campaigns.read";
    public const string Write = "campaigns.write";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Kampanyaları ve sonuçlarını görüntüleme"),
        new(Write, Group, "Kampanya oluşturma ve kayıt bağlama"),
    ];
}
