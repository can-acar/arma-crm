using Arma.SharedKernel.Tenancy;
using Arma.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Arma.Modules.Contacts.Infrastructure;

/// <summary>Yalnızca <c>dotnet ef migrations</c> için; çalışma zamanında kullanılmaz.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<ContactsDbContext>
{
    public ContactsDbContext CreateDbContext(string[] args)
    {
        var options = ModuleDbContextExtensions.DesignTimeOptions<ContactsDbContext>(ContactsDbContext.Schema);
        return new ContactsDbContext(options, new DesignTimeTenant());
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
