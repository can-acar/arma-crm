using Arma.Infrastructure.Auditing;
using Arma.Modules.Cases.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Cases.Infrastructure;

public sealed class CasesDbContext(DbContextOptions<CasesDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "cases";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<CaseType> Types => Set<CaseType>();

    public DbSet<Case> Cases => Set<Case>();

    public DbSet<CaseStep> Steps => Set<CaseStep>();

    public DbSet<CaseNote> Notes => Set<CaseNote>();

    /// <summary>Yıl içinde artan vaka sırası; satır kilidiyle artırılır.</summary>
    public async Task<long> NextCaseNumberAsync(int year, CancellationToken cancellationToken)
    {
        var rows = await Database.SqlQuery<long>($"""
            INSERT INTO cases.case_sequences (tenant_id, year, last_number) VALUES ({tenantId}, {year}, 1)
            ON CONFLICT (tenant_id, year) DO UPDATE SET last_number = case_sequences.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);
        return rows[0];
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<CaseType>(entity =>
        {
            entity.ToTable("case_types");
            entity.Property(t => t.Name).HasMaxLength(100).UseCollation("tr-TR-x-icu");
            entity.Property(t => t.Description).HasMaxLength(500);
            entity.OwnsMany(t => t.Steps, step => step.ToJson("steps"));
            entity.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(t => t.TenantId == tenantId && !t.IsDeleted);
        });

        modelBuilder.Entity<Case>(entity =>
        {
            entity.ToTable("cases");
            entity.Property(c => c.Number).HasMaxLength(20);
            entity.Property(c => c.Subject).HasMaxLength(200);
            entity.Property(c => c.Description).HasMaxLength(4000);
            entity.Property(c => c.Outcome).HasMaxLength(500);
            entity.Property(c => c.Status).HasConversion<short>();
            entity.Property(c => c.Priority).HasConversion<short>();
            entity.HasOne<CaseType>().WithMany().HasForeignKey(c => c.TypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(c => c.Steps).WithOne().HasForeignKey(s => s.CaseId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(c => c.Steps).AutoInclude();
            entity.HasIndex(c => new { c.TenantId, c.Number }).IsUnique();
            entity.HasIndex(c => new { c.TenantId, c.ContactId });
            entity.HasIndex(c => new { c.TenantId, c.Status, c.AssigneeId });
            entity.HasQueryFilter(c => c.TenantId == tenantId && !c.IsDeleted);
        });

        modelBuilder.Entity<CaseStep>(entity =>
        {
            entity.ToTable("case_steps");
            entity.Property(s => s.Id).ValueGeneratedNever();
            entity.Property(s => s.Title).HasMaxLength(200);
            entity.Property(s => s.CompletedBy).HasMaxLength(200);
            entity.Ignore(s => s.IsDone);
            entity.HasIndex(s => new { s.TenantId, s.DueOn }).HasFilter("completed_at IS NULL");
            entity.HasQueryFilter(s => s.TenantId == tenantId && !s.IsDeleted);
        });

        modelBuilder.Entity<CaseNote>(entity =>
        {
            entity.ToTable("case_notes");
            entity.Property(n => n.Body).HasMaxLength(8000);
            entity.HasOne<Case>().WithMany().HasForeignKey(n => n.CaseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(n => new { n.TenantId, n.CaseId });
            entity.HasQueryFilter(n => n.TenantId == tenantId && !n.IsDeleted);
        });
    }
}
