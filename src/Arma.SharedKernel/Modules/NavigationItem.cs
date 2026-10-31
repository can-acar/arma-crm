namespace Arma.SharedKernel.Modules;

/// <summary>Kenar çubuğunda gösterilecek bir modül bağlantısı.</summary>
/// <param name="Permission">Bağlantıyı görmek için gereken izin; null ise her oturum açmış kullanıcı görür.</param>
public sealed record NavigationItem(string Title, string Href, int Order, string? Permission = null);

public interface INavigationContributor
{
    IEnumerable<NavigationItem> GetNavigationItems();
}
