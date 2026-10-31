using Arma.Infrastructure.Persistence;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Design;

namespace Arma.Modules.Membership.Infrastructure;

/// <summary>Yalnızca <c>dotnet ef migrations</c> için; çalışma zamanında kullanılmaz.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<MembershipDbContext>
{
    public MembershipDbContext CreateDbContext(string[] args)
    {
        var options = ModuleDbContextExtensions.DesignTimeOptions<MembershipDbContext>(MembershipDbContext.Schema);
        return new MembershipDbContext(options, new DesignTimeTenant());
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
