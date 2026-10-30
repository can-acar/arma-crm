namespace Arma.SharedKernel.Auditing;

public enum AuditAction
{
    Created,
    Updated,
    Deleted,
    Viewed,
    Exported,
    Imported,
    Merged,
}

/// <summary>Denetim izinde değeri gösterilmeyen alan (TCKN, sağlık bilgisi gibi); yalnızca değiştiği kaydedilir.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SensitiveDataAttribute : Attribute;

/// <summary>Denetim izine hiç yazılmayan, türetilmiş alan (arama metni gibi).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute;

public sealed record AuditRecord(
    Guid Id,
    DateTimeOffset OccurredAt,
    string? UserName,
    string? IpAddress,
    AuditAction Action,
    string EntityType,
    string? EntityId,
    string? Summary,
    IReadOnlyList<AuditChange> Changes);

public sealed record AuditChange(string Property, string? OldValue, string? NewValue);

/// <summary>
/// Veri değişiklikleri kayıt sırasında otomatik yazılır. Bu arayüz, değişiklik olmayan olaylar içindir:
/// hassas alan görüntüleme, dışa aktarma, içe aktarma.
/// </summary>
public interface IAuditLog
{
    Task WriteAsync(AuditAction action, string entityType, string? entityId, string? summary, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditRecord>> ForEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default);
}
