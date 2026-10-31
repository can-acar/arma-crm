using Arma.Modules.Webhooks.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Webhooks.Infrastructure;

public sealed class WebhooksDbContext(DbContextOptions<WebhooksDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "webhooks";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<WebhookEndpoint> Endpoints => Set<WebhookEndpoint>();

    public DbSet<WebhookDelivery> Deliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Gönderim denemeleri sık güncellenir; denetim izine yalnızca adres değişiklikleri servisten yazılır.
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<WebhookEndpoint>(entity =>
        {
            entity.ToTable("endpoints");
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Url).HasMaxLength(500);
            entity.Property(e => e.ProtectedSecret).HasMaxLength(1000);
            entity.Property(e => e.Events).HasColumnType("text[]");
            entity.HasQueryFilter(e => e.TenantId == tenantId && !e.IsDeleted);
        });

        modelBuilder.Entity<WebhookDelivery>(entity =>
        {
            entity.ToTable("deliveries");
            entity.Property(d => d.Id).ValueGeneratedNever();
            entity.Property(d => d.EventType).HasMaxLength(50);
            // json, jsonb gibi anahtarları yeniden sıralamaz; gövde oluşturulduğu biçimde gönderilir.
            entity.Property(d => d.Payload).HasColumnType("json");
            entity.Property(d => d.Status).HasConversion<short>();
            entity.Property(d => d.LastError).HasMaxLength(500);
            entity.HasOne<WebhookEndpoint>().WithMany().HasForeignKey(d => d.EndpointId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(d => new { d.Status, d.NextAttemptAt }).HasFilter("status = 1");
            entity.HasIndex(d => new { d.TenantId, d.EndpointId, d.CreatedAt });
            entity.HasQueryFilter(d => d.TenantId == tenantId && !d.IsDeleted);
        });
    }
}
