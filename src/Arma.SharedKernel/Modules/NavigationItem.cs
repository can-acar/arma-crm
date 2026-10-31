namespace Arma.SharedKernel.Modules;

/// <summary>Kenar çubuğunda gösterilecek bir modül bağlantısı.</summary>
/// <param name="RequiredRole">Verilirse bağlantı yalnızca bu roldeki kullanıcılara gösterilir.</param>
public sealed record NavigationItem(string Title, string Href, int Order, string? RequiredRole = null);

public interface INavigationContributor
{
    IEnumerable<NavigationItem> GetNavigationItems();
}
