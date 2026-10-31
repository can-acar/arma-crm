using Arma.SharedKernel.Security;

namespace Arma.Modules.Activities;

public static class ActivitiesPermissions
{
    public const string Read = "activities.read";
    public const string Write = "activities.write";
    public const string Delete = "activities.delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "Aktiviteler", "Not, arama, toplantı ve görevleri görüntüleme"),
        new(Write, "Aktiviteler", "Aktivite ekleme, düzenleme ve görev tamamlama"),
        new(Delete, "Aktiviteler", "Aktivite silme"),
    ];
}
