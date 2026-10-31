using Arma.Infrastructure.Auditing;
using Arma.Modules.Privacy.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Privacy.Infrastructure;

public sealed class PrivacyDbContext(DbContextOptions<PrivacyDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "privacy";

    public static readonly string[] TenantTables = ["notices", "notice_versions", "consent_records", "data_requests", "anonymizations"];

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<Notice> Notices => Set<Notice>();

    public DbSet<NoticeVersion> NoticeVersions => Set<NoticeVersion>();

    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();

    public DbSet<DataRequest> Requests => Set<DataRequest>();

    public DbSet<Anonymization> Anonymizations => Set<Anonymization>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Notice>(entity =>
        {
            entity.Property(n => n.Title).HasMaxLength(200);
            entity.Property(n => n.Slug).HasMaxLength(80);
            entity.HasIndex(n => new { n.TenantId, n.Slug }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(n => n.TenantId == tenantId && !n.IsDeleted);
        });

        modelBuilder.Entity<NoticeVersion>(entity =>
        {
            entity.Property(v => v.Title).HasMaxLength(200);
            entity.HasIndex(v => new { v.NoticeId, v.Number }).IsUnique();
            entity.HasQueryFilter(v => v.TenantId == tenantId && !v.IsDeleted);
        });

        modelBuilder.Entity<ConsentRecord>(entity =>
        {
            entity.Property(c => c.Source).HasMaxLength(30);
            entity.Property(c => c.IpAddress).HasMaxLength(45);
            entity.Property(c => c.Note).HasMaxLength(500);
            entity.HasIndex(c => new { c.TenantId, c.ContactId, c.NoticeId, c.RecordedAt });
            entity.HasQueryFilter(c => c.TenantId == tenantId && !c.IsDeleted);
        });

        modelBuilder.Entity<DataRequest>(entity =>
        {
            entity.ToTable("data_requests");
            entity.Property(r => r.Type).HasConversion<short>();
            entity.Property(r => r.Channel).HasConversion<short>();
            entity.Property(r => r.Status).HasConversion<short>();
            entity.Property(r => r.RequesterName).HasMaxLength(200);
            entity.Property(r => r.Email).HasMaxLength(254);
            entity.Property(r => r.Phone).HasMaxLength(32);
            entity.Ignore(r => r.IsOpen);
            entity.HasIndex(r => new { r.TenantId, r.Status, r.DueOn });
            entity.HasIndex(r => new { r.TenantId, r.ContactId });
            entity.HasQueryFilter(r => r.TenantId == tenantId && !r.IsDeleted);
        });

        modelBuilder.Entity<Anonymization>(entity =>
        {
            entity.HasIndex(a => new { a.TenantId, a.ContactId });
            entity.HasQueryFilter(a => a.TenantId == tenantId && !a.IsDeleted);
        });
    }
}
