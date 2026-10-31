using Arma.Infrastructure.Auditing;
using Arma.Modules.Comms.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Comms.Infrastructure;

public sealed class CommsDbContext(DbContextOptions<CommsDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "comms";

    public static readonly string[] TenantTables = ["channel_consents", "messages", "mailings", "templates"];

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<ChannelConsent> Consents => Set<ChannelConsent>();

    public DbSet<OutboundMessage> Messages => Set<OutboundMessage>();

    public DbSet<Mailing> Mailings => Set<Mailing>();

    public DbSet<MessageTemplate> Templates => Set<MessageTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<ChannelConsent>(entity =>
        {
            entity.ToTable("channel_consents");
            entity.Property(c => c.Channel).HasConversion<short>();
            entity.Property(c => c.Status).HasConversion<short>();
            entity.Property(c => c.IysState).HasConversion<short>();
            entity.Property(c => c.Address).HasMaxLength(254);
            entity.Property(c => c.Source).HasMaxLength(30);
            entity.Property(c => c.IysError).HasMaxLength(500);
            entity.HasIndex(c => new { c.TenantId, c.ContactId, c.Channel }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasIndex(c => c.IysState).HasFilter("iys_state IN (1, 3)");
            entity.HasQueryFilter(c => c.TenantId == tenantId && !c.IsDeleted);
        });

        modelBuilder.Entity<OutboundMessage>(entity =>
        {
            entity.ToTable("messages");
            entity.Property(m => m.Channel).HasConversion<short>();
            entity.Property(m => m.Status).HasConversion<short>();
            entity.Property(m => m.To).HasMaxLength(254);
            entity.Property(m => m.Subject).HasMaxLength(300);
            entity.Property(m => m.Error).HasMaxLength(1000);
            entity.Property(m => m.ProviderMessageId).HasMaxLength(200);
            entity.HasIndex(m => new { m.TenantId, m.ContactId });
            entity.HasIndex(m => new { m.MailingId, m.Status });
            entity.HasQueryFilter(m => m.TenantId == tenantId && !m.IsDeleted);
        });

        modelBuilder.Entity<Mailing>(entity =>
        {
            entity.Property(m => m.Name).HasMaxLength(200);
            entity.Property(m => m.Channel).HasConversion<short>();
            entity.Property(m => m.Status).HasConversion<short>();
            entity.Property(m => m.Subject).HasMaxLength(300);
            entity.HasQueryFilter(m => m.TenantId == tenantId && !m.IsDeleted);
        });

        modelBuilder.Entity<MessageTemplate>(entity =>
        {
            entity.ToTable("templates");
            entity.Property(t => t.Name).HasMaxLength(120);
            entity.Property(t => t.Channel).HasConversion<short>();
            entity.Property(t => t.Subject).HasMaxLength(300);
            entity.HasQueryFilter(t => t.TenantId == tenantId && !t.IsDeleted);
        });
    }
}
