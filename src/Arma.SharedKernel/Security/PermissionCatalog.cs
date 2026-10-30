namespace Arma.SharedKernel.Security;

/// <summary>Kurulumdaki tüm modüllerin izinleri. Yönetici rolü bunların hepsine sahiptir.</summary>
public sealed class PermissionCatalog(IEnumerable<PermissionDefinition> permissions)
{
    private readonly Dictionary<string, PermissionDefinition> byName =
        permissions.DistinctBy(p => p.Name).ToDictionary(p => p.Name, StringComparer.Ordinal);

    public IReadOnlyCollection<PermissionDefinition> All => byName.Values;

    public bool Contains(string name) => byName.ContainsKey(name);

    public IEnumerable<IGrouping<string, PermissionDefinition>> Grouped() =>
        byName.Values.OrderBy(p => p.Name, StringComparer.Ordinal).GroupBy(p => p.Group);
}
