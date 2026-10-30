namespace Arma.SharedKernel.Modules;

/// <summary>Kenar çubuğunda gösterilecek bir modül bağlantısı.</summary>
public sealed record NavigationItem(string Title, string Href, int Order);

public interface INavigationContributor
{
    IEnumerable<NavigationItem> GetNavigationItems();
}
