using Arma.SharedKernel.Security;

namespace Arma.Modules.Events;

public static class EventsPermissions
{
    private const string Group = "Etkinlikler";

    public const string Read = "events.read";
    public const string Write = "events.write";
    public const string CheckIn = "events.checkin";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Etkinlikleri ve katılımcı listelerini görüntüleme"),
        new(Write, Group, "Etkinlik oluşturma, bilet tanımlama, katılımcı kaydetme ve ödeme işaretleme"),
        new(CheckIn, Group, "Etkinlik girişinde bilet okutma"),
    ];
}
