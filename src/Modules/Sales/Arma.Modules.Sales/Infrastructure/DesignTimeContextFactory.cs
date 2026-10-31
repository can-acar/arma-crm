using Arma.Infrastructure.Persistence;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Design;

namespace Arma.Modules.Sales.Infrastructure;

/// <summary>Yalnızca <c>dotnet ef migrations</c> için; çalışma zamanında kullanılmaz.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<SalesDbContext>
{
    public SalesDbContext CreateDbContext(string[] args)
    {
        var options = ModuleDbContextExtensions.DesignTimeOptions<SalesDbContext>(SalesDbContext.Schema);
        return new SalesDbContext(options, new DesignTimeTenant());
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
