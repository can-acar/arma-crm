namespace Arma.SharedKernel.Modules;

/// <summary>Kampanyaya bağlanabilen bir kayıt (fon, etkinlik, toplu ileti, form).</summary>
public sealed record CampaignItem(Guid Id, string Name);

/// <summary>Kampanya sonucunda gösterilen bir sayı. <see cref="Format"/>: "money" veya "number".</summary>
public sealed record CampaignMetric(string Label, decimal Value, string Format, bool CountsTowardGoal = false);

/// <summary>Kampanya dönemi; uçlar dahil, boşsa sınırsız.</summary>
public sealed record CampaignPeriod(DateOnly? From, DateOnly? To)
{
    public bool Contains(DateOnly date) => (From is null || date >= From) && (To is null || date <= To);
}

/// <summary>
/// Bir modülün kayıtlarını kampanyaya bağlanabilir kılar ve kampanya sonucuna katkısını hesaplar. Kampanya modülü
/// yalnızca bağlantıyı (tür + kimlik) saklar; sayıları kaydın sahibi modül üretir.
/// </summary>
public interface ICampaignSource
{
    /// <summary>Kalıcı tür anahtarı ("fund", "event" gibi); bağlantıyla birlikte saklanır.</summary>
    string Kind { get; }

    /// <summary>Ekranda görünen tür adı ("Fon", "Etkinlik").</summary>
    string Label { get; }

    int Order { get; }

    /// <summary>Kaydı görmek için gereken izin; yoksa tür listelenmez.</summary>
    string? Permission { get; }

    Task<IReadOnlyList<CampaignItem>> ItemsAsync(CancellationToken cancellationToken);

    /// <summary>Kaydın uygulama içi adresi ("events/{id}").</summary>
    string Href(Guid id);

    Task<IReadOnlyList<CampaignMetric>> MetricsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken);

    /// <summary>Kampanyanın ulaştığı kişiler (bağışçı, katılımcı, ileti alan, formu dolduran).</summary>
    Task<IReadOnlyCollection<Guid>> ContactIdsAsync(IReadOnlyCollection<Guid> ids, CampaignPeriod period, CancellationToken cancellationToken);
}
