using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Infrastructure.Security;

public static class SecurityServiceCollectionExtensions
{
    /// <summary>İzin politikalarını ve geçerli kullanıcı erişimini kaydeder.</summary>
    public static IServiceCollection AddArmaAuthorization(this IServiceCollection services, IEnumerable<PermissionDefinition> permissions)
    {
        services.AddSingleton(new PermissionCatalog(permissions));
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

        services.AddHttpContextAccessor();
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.AddScoped<CircuitHandler, CurrentUserCircuitHandler>();
        return services;
    }
}
