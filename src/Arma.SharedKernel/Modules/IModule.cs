using System.Reflection;
using Arma.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.SharedKernel.Modules;

/// <summary>
/// Her modül kendi servislerini, uç noktalarını ve Blazor sayfalarını bu sözleşmeyle kaydeder.
/// Host yalnızca bu arayüzü tanır; modüllerin iç tiplerine erişmez.
/// </summary>
public interface IModule
{
    string Name { get; }

    /// <summary>Modülün Blazor sayfalarını barındıran derleme; sayfası yoksa null.</summary>
    Assembly? PagesAssembly => null;

    /// <summary>Modülün tanımladığı izinler; rol ekranında listelenir.</summary>
    IEnumerable<PermissionDefinition> Permissions => [];

    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Kimlik doğrulama ve yetkilendirmeden sonra, uç noktalardan önce araya girecek ara katmanlar.</summary>
    void UseMiddleware(IApplicationBuilder app)
    {
    }

    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Modülün veritabanı şemasını güncel sürüme getirir.</summary>
    Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken);
}
