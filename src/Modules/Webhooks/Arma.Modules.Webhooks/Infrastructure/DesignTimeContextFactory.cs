using Arma.Infrastructure.Persistence;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Design;

namespace Arma.Modules.Webhooks.Infrastructure;

/// <summary>Yalnızca <c>dotnet ef migrations</c> için; çalışma zamanında kullanılmaz.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<WebhooksDbContext>
{
    public WebhooksDbContext CreateDbContext(string[] args)
    {
        var options = ModuleDbContextExtensions.DesignTimeOptions<WebhooksDbContext>(WebhooksDbContext.Schema);
        return new WebhooksDbContext(options, new DesignTimeTenant());
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
