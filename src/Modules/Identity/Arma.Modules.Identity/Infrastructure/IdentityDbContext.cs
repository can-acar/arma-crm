using Arma.Infrastructure.Persistence;
using Arma.Modules.Identity.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using OpenIddict.EntityFrameworkCore.Models;

namespace Arma.Modules.Identity.Infrastructure;

public sealed class ArmaIdentityDbContext(DbContextOptions<ArmaIdentityDbContext> options, ITenantContext tenant, TimeProvider clock)
    : IdentityDbContext<ArmaUser, ArmaRole, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";

    private readonly Guid tenantId = tenant.TenantId;

    /// <summary>Oturum çerezlerini ve API belirteçlerini koruyan anahtarlar; konteyner yeniden başlasa da geçerli kalır.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        builder.UseOpenIddict<Guid>();

        builder.Entity<ArmaUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(u => u.FullName).HasMaxLength(200).IsRequired().UseCollation("tr-TR-x-icu");
            entity.HasIndex(u => u.TenantId);
            entity.HasQueryFilter(u => u.TenantId == tenantId);
        });

        builder.Entity<ArmaRole>(entity =>
        {
            entity.ToTable("roles");
            entity.Property(r => r.Description).HasMaxLength(500);
            entity.HasIndex(r => r.TenantId);
            entity.HasQueryFilter(r => r.TenantId == tenantId);
        });

        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("role_claims");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
        builder.Entity<OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("openiddict_applications");
        builder.Entity<OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToTable("openiddict_authorizations");
        builder.Entity<OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable("openiddict_scopes");
        builder.Entity<OpenIddictEntityFrameworkCoreToken<Guid>>().ToTable("openiddict_tokens");
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<ArmaUser>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TenantId = tenantId;
            entry.Entity.CreatedAt = clock.GetUtcNow();
        }

        foreach (var entry in ChangeTracker.Entries<ArmaRole>().Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TenantId = tenantId;
        }

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

/// <summary>Yalnızca <c>dotnet ef migrations</c> için; çalışma zamanında kullanılmaz.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<ArmaIdentityDbContext>
{
    public ArmaIdentityDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextExtensions.DesignTimeOptions<ArmaIdentityDbContext>(ArmaIdentityDbContext.Schema), new DesignTimeTenant(), TimeProvider.System);

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
