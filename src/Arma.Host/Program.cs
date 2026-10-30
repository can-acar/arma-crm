using Arma.Host;
using Arma.Host.Components;
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
app.UseAntiforgery();

app.MapStaticAssets();
app.MapDefaultEndpoints();

foreach (var module in ArmaModules.All)
{
    module.MapEndpoints(app);
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(ArmaModules.PageAssemblies);

app.Run();
