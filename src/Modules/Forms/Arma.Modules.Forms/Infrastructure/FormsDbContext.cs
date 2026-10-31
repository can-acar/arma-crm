using Arma.Infrastructure.Auditing;
using Arma.Modules.Forms.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Forms.Infrastructure;

public sealed class FormsDbContext(DbContextOptions<FormsDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "forms";

    public static readonly string[] TenantTables = ["forms", "submissions"];

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<WebForm> Forms => Set<WebForm>();

    public DbSet<FormSubmission> Submissions => Set<FormSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<WebForm>(entity =>
        {
            entity.ToTable("forms");
            entity.Property(f => f.Name).HasMaxLength(200);
            entity.Property(f => f.Slug).HasMaxLength(80);
            entity.Property(f => f.Title).HasMaxLength(200);
            entity.Property(f => f.TagName).HasMaxLength(60);
            entity.Property(f => f.ConsentNoticeSlug).HasMaxLength(80);
            entity.Property(f => f.InfoNoticeSlug).HasMaxLength(80);
            entity.Property(f => f.FieldsJson).HasColumnName("fields").HasColumnType("jsonb");
            entity.Ignore(f => f.Fields);
            entity.Ignore(f => f.CreatesContact);
            entity.HasIndex(f => new { f.TenantId, f.Slug }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(f => f.TenantId == tenantId && !f.IsDeleted);
        });

        modelBuilder.Entity<FormSubmission>(entity =>
        {
            entity.ToTable("submissions");
            entity.Property(s => s.Values).HasColumnType("jsonb");
            entity.Property(s => s.IpAddress).HasMaxLength(45);
            entity.HasIndex(s => new { s.FormId, s.SubmittedAt });
            entity.HasIndex(s => new { s.TenantId, s.ContactId });
            entity.HasQueryFilter(s => s.TenantId == tenantId && !s.IsDeleted);
        });
    }
}
