using Arma.Modules.Identity.Contracts;
using System.Reflection;
using Arma.Infrastructure.Persistence;
using Arma.Modules.Identity.Application;
using Arma.Modules.Identity.Domain;
using Arma.Modules.Identity.Infrastructure;
using Arma.Modules.Identity.Web;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;

namespace Arma.Modules.Identity;

public sealed class IdentityModule : IModule, INavigationContributor
{
    /// <summary>İstekte Bearer belirteci varsa API belirtecini, yoksa oturum çerezini kullanan şema.</summary>
    public const string SmartScheme = "Arma";

    public string Name => "Kimlik";

    public Assembly PagesAssembly => typeof(IdentityModule).Assembly;

    public IEnumerable<PermissionDefinition> Permissions => IdentityPermissions.All;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ArmaIdentityDbContext>(configuration, ArmaIdentityDbContext.Schema);

        services.AddDataProtection()
            .SetApplicationName("Arma")
            .PersistKeysToDbContext<ArmaIdentityDbContext>();

        services.AddAuthentication(options =>
            {
                options.DefaultScheme = SmartScheme;
                options.DefaultChallengeScheme = SmartScheme;
            })
            .AddPolicyScheme(SmartScheme, "Arma", options => options.ForwardDefaultSelector = context =>
                context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
                    : IdentityConstants.ApplicationScheme)
            .AddIdentityCookies();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "arma.auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.LoginPath = "/account/login";
            options.LogoutPath = "/account/logout";
            options.AccessDeniedPath = "/account/access-denied";
            options.ExpireTimeSpan = TimeSpan.FromMinutes(configuration.GetValue("Arma:Security:SessionTimeoutMinutes", 60));
            options.SlidingExpiration = true;
            options.Events.OnRedirectToLogin = context => ApiAwareRedirect(context, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => ApiAwareRedirect(context, StatusCodes.Status403Forbidden);
        });

        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.FromMinutes(5));

        services.AddIdentityCore<ArmaUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddRoles<ArmaRole>()
            .AddEntityFrameworkStores<ArmaIdentityDbContext>()
            .AddSignInManager<ArmaSignInManager>()
            .AddClaimsPrincipalFactory<ArmaClaimsPrincipalFactory>()
            .AddErrorDescriber<TurkishIdentityErrorDescriber>()
            .AddDefaultTokenProviders();

        var allowHttp = configuration.GetValue<bool>("Arma:Security:AllowHttp");
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<ArmaIdentityDbContext>().ReplaceDefaultEntities<Guid>())
            .AddServer(options =>
            {
                options.SetTokenEndpointUris("connect/token");
                options.AllowClientCredentialsFlow();
                options.SetAccessTokenLifetime(TimeSpan.FromHours(1));

                // Belirteçler ASP.NET Data Protection ile korunur; anahtarlar veritabanında kalıcıdır.
                // Geçici anahtarlar yalnızca OpenIddict'in zorunlu tuttuğu imza ayarı için gerekir.
                options.UseDataProtection();
                options.AddEphemeralEncryptionKey().AddEphemeralSigningKey();

                var aspNetCore = options.UseAspNetCore().EnableTokenEndpointPassthrough();
                if (allowHttp)
                {
                    aspNetCore.DisableTransportSecurityRequirement();
                }
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseDataProtection();
                options.UseAspNetCore();
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        services.AddSingleton<SetupState>();
        services.AddScoped<SetupService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<RoleAdminService>();
        services.AddScoped<ApiClientService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddSingleton<INavigationContributor>(this);
    }

    public void UseMiddleware(IApplicationBuilder app) => IdentityMiddleware.Use(app);

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => IdentityEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArmaIdentityDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ArmaRole>>();
        await EnsureRoleAsync(roles, new ArmaRole(SystemRoles.Administrator)
        {
            Description = "Tüm izinlere sahiptir; iki adımlı doğrulama zorunludur.",
            IsSystem = true,
            RequiresTwoFactor = true,
        });
        await EnsureRoleAsync(roles, new ArmaRole(SystemRoles.Staff)
        {
            Description = "Kişi kayıtlarını ve aktiviteleri görür, düzenler.",
            IsSystem = true,
        }, "contacts.read", "contacts.write", "activities.read", "activities.write");
    }

    public IEnumerable<NavigationItem> GetNavigationItems() =>
    [
        new("Kullanıcılar", "admin/users", 900, IdentityPermissions.ManageUsers),
        new("Roller", "admin/roles", 910, IdentityPermissions.ManageRoles),
        new("API istemcileri", "admin/api-clients", 920, IdentityPermissions.ManageApiClients),
    ];

    private static async Task EnsureRoleAsync(RoleManager<ArmaRole> roles, ArmaRole role, params string[] permissions)
    {
        if (await roles.RoleExistsAsync(role.Name!))
        {
            return;
        }

        await roles.CreateAsync(role);
        foreach (var permission in permissions)
        {
            await roles.AddClaimAsync(role, new System.Security.Claims.Claim(ArmaClaimTypes.Permission, permission));
        }
    }

    private static Task ApiAwareRedirect(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = statusCode;
        }
        else
        {
            context.Response.Redirect(context.RedirectUri);
        }

        return Task.CompletedTask;
    }
}
