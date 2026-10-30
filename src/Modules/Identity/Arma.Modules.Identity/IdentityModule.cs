using System.Reflection;
using Arma.Modules.Identity.Application;
using Arma.Modules.Identity.Domain;
using Arma.Modules.Identity.Infrastructure;
using Arma.Modules.Identity.Web;
using Arma.SharedKernel.Modules;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.Modules.Identity;

public sealed class IdentityModule : IModule
{
    public const string LoginPath = "/account/login";

    public string Name => "Kimlik";

    public Assembly PagesAssembly => typeof(IdentityModule).Assembly;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("arma")
            ?? throw new InvalidOperationException("'ConnectionStrings:arma' yapılandırması eksik.");

        services.AddDbContext<ArmaIdentityDbContext>(options => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", ArmaIdentityDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddDataProtection()
            .SetApplicationName("Arma")
            .PersistKeysToDbContext<ArmaIdentityDbContext>();

        services.AddIdentity<ArmaUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddEntityFrameworkStores<ArmaIdentityDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<TurkishIdentityErrorDescriber>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "arma.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.LoginPath = LoginPath;
            options.AccessDeniedPath = LoginPath;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;

            // API çağrıları giriş sayfasına yönlendirilmez, durum kodu alır.
            options.Events.OnRedirectToLogin = context => RedirectOrStatus(context.HttpContext, context.RedirectUri, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => RedirectOrStatus(context.HttpContext, context.RedirectUri, StatusCodes.Status403Forbidden);
        });

        services.AddScoped<UserProvisioning>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AccountEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArmaIdentityDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(UserProvisioning.AdministratorRole))
        {
            await roles.CreateAsync(new IdentityRole<Guid>(UserProvisioning.AdministratorRole) { Id = Guid.CreateVersion7() });
        }
    }

    private static Task RedirectOrStatus(HttpContext context, string redirectUri, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = statusCode;

            // Durum kodu sayfası (not-found) API yanıtının yerine geçmesin.
            context.Features.Get<IStatusCodePagesFeature>()?.Enabled = false;
        }
        else
        {
            context.Response.Redirect(redirectUri);
        }

        return Task.CompletedTask;
    }
}
