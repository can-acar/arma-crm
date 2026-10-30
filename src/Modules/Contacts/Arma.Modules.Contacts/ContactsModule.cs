using System.Reflection;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Infrastructure;
using Arma.Modules.Contacts.Web;
using Arma.SharedKernel.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Contacts;

public sealed class ContactsModule : IModule, INavigationContributor
{
    public string Name => "Kişiler";

    public Assembly PagesAssembly => typeof(ContactsModule).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("arma")
            ?? throw new InvalidOperationException("'ConnectionStrings:arma' yapılandırması eksik.");

        services.AddScoped<TenantConnectionInterceptor>();
        services.AddDbContext<ContactsDbContext>((sp, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ContactsDbContext.Schema))
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<TenantConnectionInterceptor>()));

        services.AddScoped<ContactService>();
        services.AddScoped<IContactDirectory>(sp => sp.GetRequiredService<ContactService>());
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => ContactEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ContactsDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() => [new NavigationItem("Kişiler", "contacts", 10)];
}
