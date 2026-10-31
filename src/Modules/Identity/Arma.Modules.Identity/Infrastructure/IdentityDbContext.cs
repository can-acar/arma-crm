using Arma.Modules.Identity.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Identity.Infrastructure;

public sealed class ArmaIdentityDbContext(DbContextOptions<ArmaIdentityDbContext> options, ITenantContext tenant, TimeProvider clock)
    : IdentityDbContext<ArmaUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";

    private readonly Guid tenantId = tenant.TenantId;

    /// <summary>Oturum çerezlerini koruyan anahtarlar; konteyner yenilense de oturumlar düşmez.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.Entity<ArmaUser>(entity =>
        {
            entity.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            entity.HasIndex(u => u.TenantId);
            entity.HasQueryFilter(u => u.TenantId == tenantId);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ArmaUser>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TenantId = tenantId;
            entry.Entity.CreatedAt = clock.GetUtcNow();
        }

        return base.SaveChangesAsync(cancellationToken);
    }
}
