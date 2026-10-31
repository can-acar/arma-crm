using Arma.SharedKernel.Security;

namespace Arma.Modules.Contacts;

public static class ContactsPermissions
{
    private const string Group = "Kişiler";

    public const string Read = "contacts.read";
    public const string Write = "contacts.write";
    public const string Delete = "contacts.delete";
    public const string Export = "contacts.export";
    public const string ViewSensitive = "contacts.sensitive";
    public const string Fields = "contacts.fields";
    public const string Import = "contacts.import";

    public static readonly PermissionDefinition[] All =
    [
        new(Read, Group, "Kişi ve kurum kayıtlarını görüntüleme"),
        new(Write, Group, "Kişi ve kurum ekleme, düzenleme"),
        new(Delete, Group, "Kişi ve kurum silme"),
        new(Export, Group, "Kayıtları dışa aktarma"),
        new(ViewSensitive, Group, "TCKN gibi hassas alanları maskesiz görme"),
        new(Fields, Group, "Kişi kartına ek alan tanımlama"),
        new(Import, Group, "Eski sistemden (CiviCRM) veri aktarma"),
    ];
}
