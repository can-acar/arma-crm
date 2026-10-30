namespace Arma.Modules.Activities.Contracts;

public enum ActivityKind
{
    Note = 1,
    Call = 2,
    Meeting = 3,
    Task = 4,
    Email = 5,
    Sms = 6,

    /// <summary>Başka bir modülün düştüğü kayıt (bağış alındı, üyelik yenilendi gibi).</summary>
    System = 9,
}

/// <summary>
/// Bir modülün, kişinin zaman çizelgesine kayıt düşmek için yayımladığı mesaj. Aynı kaynak
/// (<see cref="SourceModule"/> + <see cref="SourceId"/>) ikinci kez gelirse kayıt güncellenir, çoğaltılmaz.
/// </summary>
public sealed record RecordTimelineEntry(
    IReadOnlyList<Guid> ContactIds,
    ActivityKind Kind,
    string Subject,
    string? Body,
    DateTimeOffset OccurredAt,
    string SourceModule,
    string SourceId);
