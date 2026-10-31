using Arma.Infrastructure.Auditing;
using Arma.Infrastructure.Persistence;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arma.Modules.Audit;

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options, ITenantContext tenant) : DbContext(options)
{
    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AuditEntry.Schema);
        modelBuilder.MapAuditLog(ownsTable: true);
        modelBuilder.Entity<AuditEntry>().HasQueryFilter(e => e.TenantId == tenantId);
    }
}

/// <summary>Yalnızca <c>dotnet ef migrations</c> için.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextExtensions.DesignTimeOptions<AuditDbContext>(AuditEntry.Schema), new DesignTimeTenant());

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
