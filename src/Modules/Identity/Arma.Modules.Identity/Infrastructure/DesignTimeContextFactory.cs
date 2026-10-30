using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arma.Modules.Identity.Infrastructure;

/// <summary>Yalnızca <c>dotnet ef migrations</c> için; çalışma zamanında kullanılmaz.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<ArmaIdentityDbContext>
{
    public ArmaIdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ArmaIdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=arma", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ArmaIdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ArmaIdentityDbContext(options, new DesignTimeTenant(), TimeProvider.System);
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
