using Arma.Infrastructure.Auditing;
using Arma.Modules.Membership.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Membership.Infrastructure;

public sealed class MembershipDbContext(DbContextOptions<MembershipDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "membership";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<MembershipType> Types => Set<MembershipType>();

    public DbSet<Member> Members => Set<Member>();

    public DbSet<DuesPayment> Payments => Set<DuesPayment>();

    /// <summary>Boşluksuz artan üye numarası; satır kilidiyle artırılır, çağıran işlemin içinde çalışmalıdır.</summary>
    public async Task<long> NextMemberNumberAsync(CancellationToken cancellationToken)
    {
        var rows = await Database.SqlQuery<long>($"""
            INSERT INTO membership.number_sequences (tenant_id, last_number) VALUES ({tenantId}, 1)
            ON CONFLICT (tenant_id) DO UPDATE SET last_number = number_sequences.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);
        return rows[0];
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<MembershipType>(entity =>
        {
            entity.ToTable("types");
            entity.Property(t => t.Name).HasMaxLength(120).UseCollation("tr-TR-x-icu");
            entity.Property(t => t.Description).HasMaxLength(500);
            entity.Property(t => t.Fee).HasPrecision(18, 2);
            entity.Ignore(t => t.IsLifetime);
            entity.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(t => t.TenantId == tenantId && !t.IsDeleted);
        });

        modelBuilder.Entity<Member>(entity =>
        {
            entity.ToTable("members");
            entity.Property(m => m.CancelReason).HasMaxLength(300);
            entity.Property(m => m.Note).HasMaxLength(1000);
            entity.HasOne<MembershipType>().WithMany().HasForeignKey(m => m.TypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(m => new { m.TenantId, m.Number }).IsUnique();
            entity.Property(m => m.ExternalKey).HasMaxLength(100);
            entity.HasIndex(m => new { m.TenantId, m.ExternalKey }).IsUnique().HasFilter("external_key IS NOT NULL AND NOT is_deleted");
            entity.HasIndex(m => new { m.TenantId, m.ContactId });
            entity.HasIndex(m => new { m.TenantId, m.EndOn });
            entity.HasQueryFilter(m => m.TenantId == tenantId && !m.IsDeleted);
        });

        modelBuilder.Entity<DuesPayment>(entity =>
        {
            entity.ToTable("payments");
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.Property(p => p.Method).HasConversion<short>();
            entity.Property(p => p.Note).HasMaxLength(500);
            entity.HasOne<Member>().WithMany().HasForeignKey(p => p.MemberId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(p => new { p.TenantId, p.PaidOn });
            entity.HasQueryFilter(p => p.TenantId == tenantId && !p.IsDeleted);
        });
    }
}
