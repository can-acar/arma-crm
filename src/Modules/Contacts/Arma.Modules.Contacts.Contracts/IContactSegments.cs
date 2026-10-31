namespace Arma.Modules.Contacts.Contracts;

public sealed record SegmentInfo(Guid Id, string Name);

/// <summary>Gelişmiş aramada kaydedilmiş aramalar; otomasyon kuralları bunları hedef kitle olarak kullanır.</summary>
public interface IContactSegments
{
    Task<IReadOnlyList<SegmentInfo>> SegmentsAsync(CancellationToken cancellationToken = default);

    /// <summary>Kayıtlı aramaya bugün uyan kişiler; arama silinmişse null.</summary>
    Task<IReadOnlyList<Guid>?> MatchAsync(Guid segmentId, CancellationToken cancellationToken = default);
}
