using Arma.SharedKernel.Domain;

namespace Arma.Modules.Contacts.Domain;

/// <summary>Adıyla saklanan gelişmiş arama; ölçütler JSON olarak tutulur, her açılışta yeniden çalışır.</summary>
public sealed class SavedSearch : Entity
{
    private SavedSearch()
    {
    }

    public string Name { get; private set; } = "";

    public string Criteria { get; private set; } = "[]";

    public static Result<SavedSearch> Create(string name, string criteria)
    {
        var search = new SavedSearch();
        var updated = search.Update(name, criteria);
        return updated.IsSuccess ? search : updated.Error!;
    }

    public Result<bool> Update(string name, string criteria)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new Error("searches.name_required", "Arama için bir ad girin.");
        }

        (Name, Criteria) = (name.Trim(), criteria);
        return true;
    }
}
