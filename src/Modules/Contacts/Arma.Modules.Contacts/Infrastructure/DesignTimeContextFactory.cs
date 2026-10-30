using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Arma.Modules.Contacts.Infrastructure;

/// <summary>Yalnızca <c>dotnet ef migrations</c> için; çalışma zamanında kullanılmaz.</summary>
internal sealed class DesignTimeContextFactory : IDesignTimeDbContextFactory<ContactsDbContext>
{
    public ContactsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ContactsDbContext>()
            .UseNpgsql("Host=localhost;Database=arma", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ContactsDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ContactsDbContext(options, new DesignTimeTenant(), TimeProvider.System);
    }

    private sealed class DesignTimeTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
    }
}
