using Arma.Infrastructure.Persistence;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Design;

namespace Arma.Modules.Automation.Infrastructure;

/// <summary>Yalnızca <c>dotnet ef migrations</c> için; çalışma zamanında kullanılmaz.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<AutomationDbContext>
{
    public AutomationDbContext CreateDbContext(string[] args)
    {
        var options = ModuleDbContextExtensions.DesignTimeOptions<AutomationDbContext>(AutomationDbContext.Schema);
        return new AutomationDbContext(options, new DesignTimeTenant());
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
