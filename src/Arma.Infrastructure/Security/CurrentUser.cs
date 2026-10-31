using System.Security.Claims;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Http;

namespace Arma.Infrastructure.Security;

/// <summary>
/// HTTP isteğinde kullanıcıyı <see cref="HttpContext"/>'ten, Blazor devresinde (circuit)
/// <see cref="AuthenticationStateProvider"/>'dan alır. Devrede HttpContext güvenilir değildir.
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    internal ClaimsPrincipal? CircuitPrincipal { get; set; }

    public ClaimsPrincipal Principal => CircuitPrincipal ?? httpContextAccessor.HttpContext?.User ?? Anonymous;
}

/// <summary>Devre açıldığında ve oturum durumu değiştiğinde <see cref="CurrentUser"/>'ı günceller.</summary>
public sealed class CurrentUserCircuitHandler(AuthenticationStateProvider authenticationStateProvider, CurrentUser currentUser)
    : CircuitHandler, IDisposable
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationChanged;
        currentUser.CircuitPrincipal = (await authenticationStateProvider.GetAuthenticationStateAsync()).User;
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> task) =>
        _ = UpdateAsync(task);

    private async Task UpdateAsync(Task<AuthenticationState> task) =>
        currentUser.CircuitPrincipal = (await task).User;

    public void Dispose() => authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationChanged;
}
