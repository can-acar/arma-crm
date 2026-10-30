using Arma.SharedKernel.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Arma.Infrastructure.Auditing;

/// <summary>
/// Denetim izi satırı. Yalnızca eklenir; veritabanı tetikleyicisi güncelleme ve silmeyi engeller.
/// Tablo Denetim modülünün şemasındadır; diğer modüller aynı tabloya kendi DbContext'leri üzerinden,
/// veri değişikliğiyle aynı işlemde (transaction) yazar.
/// </summary>
public sealed class AuditEntry
{
    public const string Schema = "audit";
    public const string Table = "audit_log";

    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public string? UserId { get; init; }

    public string? UserName { get; init; }

    public string? IpAddress { get; init; }

    public AuditAction Action { get; init; }

    public required string EntityType { get; init; }

    public string? EntityId { get; init; }

    public string? Summary { get; init; }

    /// <summary>Değişen alanlar: <c>[{"p":"Email","o":"eski","n":"yeni"}]</c>.</summary>
    public string? Changes { get; init; }
}

public static class AuditModelBuilderExtensions
{
    /// <summary>Denetim tablosunu bu DbContext'e ekler. Tabloyu yalnızca Denetim modülü oluşturur.</summary>
    public static ModelBuilder MapAuditLog(this ModelBuilder modelBuilder, bool ownsTable = false)
    {
        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable(AuditEntry.Table, AuditEntry.Schema, table =>
            {
                if (!ownsTable)
                {
                    table.ExcludeFromMigrations();
                }
            });
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.EntityType).HasMaxLength(100);
            entity.Property(e => e.EntityId).HasMaxLength(100);
            entity.Property(e => e.UserId).HasMaxLength(100);
            entity.Property(e => e.UserName).HasMaxLength(200);
            entity.Property(e => e.IpAddress).HasMaxLength(64);
            entity.Property(e => e.Summary).HasMaxLength(500);
            entity.Property(e => e.Changes).HasColumnType("jsonb");
            entity.HasIndex(e => new { e.TenantId, e.EntityType, e.EntityId });
            entity.HasIndex(e => new { e.TenantId, e.OccurredAt });
        });
        return modelBuilder;
    }
}
