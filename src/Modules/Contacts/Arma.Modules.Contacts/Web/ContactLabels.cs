using Arma.Modules.Contacts.Contracts;

namespace Arma.Modules.Contacts.Web;

internal static class ContactLabels
{
    public static string Type(ContactType type) => type switch
    {
        ContactType.Organization => "Kurum",
        ContactType.Household => "Hane",
        _ => "Kişi",
    };

    /// <summary>Etiket rengine göre rozet sınıfları.</summary>
    public static string TagClass(string color) => color switch
    {
        "red" => "bg-red-100 text-red-800 dark:bg-red-950 dark:text-red-200",
        "amber" => "bg-amber-100 text-amber-800 dark:bg-amber-950 dark:text-amber-200",
        "green" => "bg-green-100 text-green-800 dark:bg-green-950 dark:text-green-200",
        "blue" => "bg-blue-100 text-blue-800 dark:bg-blue-950 dark:text-blue-200",
        "purple" => "bg-purple-100 text-purple-800 dark:bg-purple-950 dark:text-purple-200",
        _ => "bg-secondary text-secondary-foreground",
    };
}
