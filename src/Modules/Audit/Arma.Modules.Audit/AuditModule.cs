using System.Reflection;
using Arma.Infrastructure.Auditing;
using Arma.Infrastructure.Persistence;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Audit;

public sealed class AuditModule : IModule, INavigationContributor
{
    public const string ReadPermission = "audit.read";

    public string Name => "Denetim";

    public Assembly PagesAssembly => typeof(AuditModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions =>
        [new(ReadPermission, "Denetim", "Denetim izini (kim, ne zaman, neyi değiştirdi) görüntüleme")];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AuditDbContext>(configuration, AuditEntry.Schema);
        services.AddScoped<AuditLogService>();
        services.AddScoped<IAuditLog>(sp => sp.GetRequiredService<AuditLogService>());
        services.AddSingleton<INavigationContributor>(this);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/audit", (AuditLogService service, string? entityType, string? entityId, string? user, AuditAction? action, DateOnly? from, DateOnly? to, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
                service.SearchAsync(new AuditQuery(entityType, entityId, user, action, from, to, page, pageSize), ct))
            .WithTags("Denetim")
            .RequireAuthorization(ReadPermission);
    }

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AuditDbContext>().Database.MigrateAsync(cancellationToken);
    }

    public IEnumerable<NavigationItem> GetNavigationItems() => [new("Denetim izi", "admin/audit", 950, ReadPermission)];
}
