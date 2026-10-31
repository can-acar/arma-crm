using Arma.Infrastructure.Auditing;
using Arma.Modules.Automation.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Automation.Infrastructure;

public sealed class AutomationDbContext(DbContextOptions<AutomationDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "automation";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<Rule> Rules => Set<Rule>();

    public DbSet<RuleRun> Runs => Set<RuleRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Rule>(entity =>
        {
            entity.ToTable("rules");
            entity.Property(r => r.Name).HasMaxLength(150);
            entity.Property(r => r.Trigger).HasConversion<short>();
            entity.Property(r => r.SourceModule).HasMaxLength(40);
            entity.Property(r => r.SubjectContains).HasMaxLength(200);
            entity.Property(r => r.ActionsJson).HasColumnType("jsonb");
            entity.Ignore(r => r.Actions);
            entity.HasQueryFilter(r => r.TenantId == tenantId && !r.IsDeleted);
        });

        modelBuilder.Entity<RuleRun>(entity =>
        {
            entity.ToTable("runs");
            entity.Property(r => r.Key).HasMaxLength(200);
            entity.Property(r => r.Status).HasConversion<short>();
            entity.HasOne<Rule>().WithMany().HasForeignKey(r => r.RuleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(r => new { r.TenantId, r.RuleId, r.ContactId, r.Key }).IsUnique();
            entity.HasIndex(r => new { r.TenantId, r.ContactId });
            entity.HasQueryFilter(r => r.TenantId == tenantId && !r.IsDeleted);
        });
    }
}
