using Arma.Infrastructure.Persistence;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Design;

namespace Arma.Modules.Forms.Infrastructure;

/// <summary>Yalnızca <c>dotnet ef migrations</c> için; çalışma zamanında kullanılmaz.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<FormsDbContext>
{
    public FormsDbContext CreateDbContext(string[] args)
    {
        var options = ModuleDbContextExtensions.DesignTimeOptions<FormsDbContext>(FormsDbContext.Schema);
        return new FormsDbContext(options, new DesignTimeTenant());
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
