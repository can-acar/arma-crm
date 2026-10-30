namespace Arma.SharedKernel.Modules;

/// <summary>Panoda tek sayı gösteren kutu.</summary>
public sealed record DashboardTile(string Title, string Value, string? Hint, string? Href, int Order, string? Permission);

public sealed record ChartPoint(string Label, decimal Value);

/// <summary>Panoda sütun grafik. <see cref="Format"/> değerin nasıl yazılacağını söyler: "money" veya "number".</summary>
public sealed record DashboardChart(string Title, IReadOnlyList<ChartPoint> Points, string Format, int Order, string? Permission);

/// <summary>Modüllerin panoya kendi sayılarını ve grafiklerini eklediği nokta.</summary>
public interface IDashboardContributor
{
    Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<DashboardChart>> ChartsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DashboardChart>>([]);
}
