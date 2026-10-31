using Arma.Infrastructure.Auditing;
using Arma.Modules.Grants.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Grants.Infrastructure;

public sealed class GrantsDbContext(DbContextOptions<GrantsDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "grants";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<Grant> Grants => Set<Grant>();

    public DbSet<GrantMilestone> Milestones => Set<GrantMilestone>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Grant>(entity =>
        {
            entity.ToTable("grants");
            entity.Property(g => g.Title).HasMaxLength(200);
            entity.Property(g => g.Program).HasMaxLength(200);
            entity.Property(g => g.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(g => g.Requested).HasPrecision(18, 2);
            entity.Property(g => g.Awarded).HasPrecision(18, 2);
            entity.Property(g => g.Status).HasConversion<short>();
            entity.Property(g => g.Description).HasMaxLength(4000);
            entity.HasMany(g => g.Milestones).WithOne().HasForeignKey(m => m.GrantId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(g => g.Milestones).AutoInclude();
            entity.HasIndex(g => new { g.TenantId, g.FunderId });
            entity.HasIndex(g => new { g.TenantId, g.Status });
            entity.HasQueryFilter(g => g.TenantId == tenantId && !g.IsDeleted);
        });

        modelBuilder.Entity<GrantMilestone>(entity =>
        {
            entity.ToTable("grant_milestones");
            entity.Property(m => m.Id).ValueGeneratedNever();
            entity.Property(m => m.Kind).HasConversion<short>();
            entity.Property(m => m.Title).HasMaxLength(200);
            entity.Property(m => m.Amount).HasPrecision(18, 2);
            entity.Ignore(m => m.IsDone);
            entity.HasIndex(m => new { m.TenantId, m.DueOn }).HasFilter("completed_on IS NULL");
            entity.HasQueryFilter(m => m.TenantId == tenantId && !m.IsDeleted);
        });
    }
}
