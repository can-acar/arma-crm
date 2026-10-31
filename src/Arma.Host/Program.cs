using Arma.Payments;
using Arma.Modules.Invoicing.Pipeline;
using Arma.Host;
using Arma.Host.Components;
using Arma.Infrastructure.Messaging;
using Arma.Infrastructure.Security;
using Arma.SharedKernel.Tenancy;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
ArmaModules.LoadPlugins(builder.Configuration, builder.Environment.ContentRootPath);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ITenantContext, ConfiguredTenantContext>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddArmaAuthorization(ArmaModules.All.SelectMany(m => m.Permissions));

// Ters vekil (nginx, IIS ARR, Caddy) arkasında HTTPS bilgisinin doğru görünmesi için.
// Yalnızca uygulamaya doğrudan erişim kapalıysa açılmalı.
var trustForwardedHeaders = builder.Configuration.GetValue<bool>("Arma:Security:TrustForwardedHeaders");
if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

foreach (var module in ArmaModules.All)
{
    module.AddServices(builder.Services, builder.Configuration);
}

// Ödeme sağlayıcıları: yerleşik olanlar ve modüllerle eklentilerdeki IPaymentProvider sınıfları kendiliğinden bulunur.
builder.Services.AddArmaPayments().AddProvidersFrom(ArmaModules.ModuleAssemblies);

// e-Fatura entegratörleri: yerleşik olanlar ve eklentilerdeki IEInvoiceProvider sınıfları kendiliğinden bulunur.
builder.Services.AddArmaEInvoicing().AddProvidersFrom(ArmaModules.ModuleAssemblies);

builder.AddArmaMessaging(ArmaModules.ModuleAssemblies);
builder.Services.AddHostedService<Arma.Infrastructure.Jobs.ScheduledJobRunner>();

builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    string[] cultures = ["tr-TR", "en-US"];
    options.SetDefaultCulture(cultures[0]).AddSupportedCultures(cultures).AddSupportedUICultures(cultures);
});

// Antiforgery X-Frame-Options, Blazor da frame-ancestors başlığını her yanıta ekler. Web formları (/f/...) başka sitelere
// gömülebilsin diye ikisini de kapatıp başlıkları aşağıda kendimiz yazıyoruz.
builder.Services.AddAntiforgery(options => options.SuppressXFrameOptionsHeader = true);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

foreach (var plugin in ArmaModules.Plugins)
{
    if (plugin.State == Arma.Infrastructure.Plugins.PluginState.Failed)
    {
        app.Logger.LogError("Eklenti yüklenemedi ({Plugin}): {Error}", plugin.Id, plugin.Error);
    }
    else
    {
        app.Logger.LogInformation("Eklenti {Plugin} {Version}: {State}", plugin.Id, plugin.Manifest?.Version, plugin.State);
    }
}

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

if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
else
{
    app.MapOpenApi().AllowAnonymous();
}

// API yanıtları (401, 403, 404) olduğu gibi döner; yalnızca sayfa isteklerinde "bulunamadı" sayfası gösterilir.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/connect"),
    pages => pages.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();
app.Use((context, next) =>
{
    if (!context.Request.Path.StartsWithSegments("/f"))
    {
        context.Response.Headers.XFrameOptions = "SAMEORIGIN";
        context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'self'";
    }

    return next(context);
});
app.UseRequestLocalization();

app.UseAuthentication();
foreach (var module in ArmaModules.All)
{
    module.UseMiddleware(app);
}

app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapDefaultEndpoints();

foreach (var module in ArmaModules.All)
{
    module.MapEndpoints(app);
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode(options => options.ContentSecurityFrameAncestorsPolicy = null)
    .AddAdditionalAssemblies(ArmaModules.PageAssemblies);

app.Run();

/// <summary>Entegrasyon testlerinin <c>WebApplicationFactory</c> ile uygulamayı başlatabilmesi için.</summary>
public partial class Program;
