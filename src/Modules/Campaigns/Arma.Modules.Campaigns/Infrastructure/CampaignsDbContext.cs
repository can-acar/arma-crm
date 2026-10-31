using Arma.Infrastructure.Auditing;
using Arma.Modules.Campaigns.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Campaigns.Infrastructure;

public sealed class CampaignsDbContext(DbContextOptions<CampaignsDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "campaigns";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<Campaign> Campaigns => Set<Campaign>();

    public DbSet<CampaignLink> Links => Set<CampaignLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Campaign>(entity =>
        {
            entity.ToTable("campaigns");
            entity.Property(c => c.Name).HasMaxLength(150);
            entity.Property(c => c.Description).HasMaxLength(2000);
            entity.Property(c => c.Status).HasConversion<short>();
            entity.Property(c => c.GoalAmount).HasPrecision(18, 2);
            entity.HasMany(c => c.Links).WithOne().HasForeignKey(l => l.CampaignId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(c => c.Links).AutoInclude();
            entity.HasQueryFilter(c => c.TenantId == tenantId && !c.IsDeleted);
        });

        modelBuilder.Entity<CampaignLink>(entity =>
        {
            entity.ToTable("links");
            entity.Property(l => l.Id).ValueGeneratedNever();
            entity.Property(l => l.Kind).HasMaxLength(40);
            entity.HasIndex(l => new { l.CampaignId, l.Kind, l.ItemId }).IsUnique().HasFilter("is_deleted = false");
            entity.HasIndex(l => new { l.TenantId, l.Kind, l.ItemId });
            entity.HasQueryFilter(l => l.TenantId == tenantId && !l.IsDeleted);
        });
    }
}
