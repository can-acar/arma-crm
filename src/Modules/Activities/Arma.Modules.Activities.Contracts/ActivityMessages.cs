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

/// <summary>Kaynağı silinen veya geçersiz olan (ödemesi başarısız bağış gibi) zaman çizelgesi kaydını kaldırır.</summary>
public sealed record RemoveTimelineEntry(string SourceModule, string SourceId);

/// <summary>
/// Başka bir modülün (otomasyon kuralı gibi) açtığı görev. Atanan kullanıcı pasifse veya bulunamazsa görev atanmadan açılır.
/// </summary>
public sealed record CreateFollowUpTask(IReadOnlyList<Guid> ContactIds, string Subject, string? Body, DateTimeOffset? DueAt, Guid? AssignedToUserId);
