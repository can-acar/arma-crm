using System.Security.Claims;
using Arma.SharedKernel.Identity;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace Arma.Host.Identity;

/// <summary>
/// İstek ya da devre başına bir örnek. HTTP isteklerinde ara katman, Blazor devrelerinde
/// <see cref="CurrentUserCircuitHandler"/> kullanıcıyı atar. Etkileşimli devrelerde HttpContext
/// güvenilir olmadığı için IHttpContextAccessor kullanılmaz.
/// </summary>
internal sealed class CurrentUser : ICurrentUser
{
    private ClaimsPrincipal principal = new();

    public Guid? UserId => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public void Set(ClaimsPrincipal user) => principal = user;
}

internal sealed class CurrentUserCircuitHandler(AuthenticationStateProvider authentication, CurrentUser currentUser) : CircuitHandler, IDisposable
{
    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        currentUser.Set((await authentication.GetAuthenticationStateAsync()).User);
        authentication.AuthenticationStateChanged += OnChanged;
    }

    public void Dispose() => authentication.AuthenticationStateChanged -= OnChanged;

    private async void OnChanged(Task<AuthenticationState> state) => currentUser.Set((await state).User);
}

internal static class CurrentUserExtensions
{
    public static IServiceCollection AddCurrentUser(this IServiceCollection services)
    {
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.AddScoped<CircuitHandler, CurrentUserCircuitHandler>();
        return services;
    }

    public static IApplicationBuilder UseCurrentUser(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.RequestServices.GetRequiredService<CurrentUser>().Set(context.User);
            return next(context);
        });
}
