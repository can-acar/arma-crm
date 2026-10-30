using Microsoft.AspNetCore.Authorization;
using Arma.Host;
using Arma.Host.Components;
using Arma.Modules.Identity.Application;
using Arma.SharedKernel.Tenancy;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITenantContext, ConfiguredTenantContext>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

foreach (var module in ArmaModules.All)
{
    module.AddServices(builder.Services, builder.Configuration);
}

// Varsayılan olarak her sayfa ve uç nokta oturum ister; istisnalar AllowAnonymous ile açılır.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddCascadingAuthenticationState();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Kurulum ve güncellemede şema, uygulama açılışında değil ayrı bir komutla güncellenir:
//   dotnet Arma.Host.dll migrate
var migrateOnly = args.Contains("migrate", StringComparer.OrdinalIgnoreCase);
if (migrateOnly || app.Configuration.GetValue<bool>("Arma:MigrateOnStartup"))
{
    foreach (var module in ArmaModules.All)
    {
        app.Logger.LogInformation("{Module} modülünün veritabanı güncelleniyor", module.Name);
        await module.MigrateAsync(app.Services, CancellationToken.None);
    }

    if (migrateOnly)
    {
        return;
    }
}

// İlk yönetici: dotnet Arma.Host.dll create-admin <e-posta> [görünen ad]
// Parola komut satırında görünmemesi için standart girişten okunur.
if (args is ["create-admin", var adminEmail, ..])
{
    Console.Error.Write("Parola: ");
    var password = Console.ReadLine() ?? "";
    await using var scope = app.Services.CreateAsyncScope();
    var provisioning = scope.ServiceProvider.GetRequiredService<UserProvisioning>();
    var created = await provisioning.CreateAsync(adminEmail, args.ElementAtOrDefault(2) ?? adminEmail, password, administrator: true, CancellationToken.None);
    if (!created.IsSuccess)
    {
        Console.Error.WriteLine(created.Error!.Message);
        Environment.ExitCode = 1;
    }
    else
    {
        Console.Error.WriteLine("Yönetici oluşturuldu.");
    }

    return;
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
else
{
    app.MapOpenApi();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapDefaultEndpoints();

foreach (var module in ArmaModules.All)
{
    module.MapEndpoints(app);
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(ArmaModules.PageAssemblies);

app.Run();

public partial class Program;
