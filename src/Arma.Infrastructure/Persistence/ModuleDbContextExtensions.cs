using Arma.Infrastructure.Auditing;
using Arma.Infrastructure.Security;
using Arma.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arma.Infrastructure.Persistence;

public static class ModuleDbContextExtensions
{
    public const string ConnectionStringName = "arma";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>
    /// Bir modülün DbContext'ini kaydeder: kendi şeması, kendi migration tablosu, snake_case adlar,
    /// kiracı oturum değişkeni ve kayıt damgaları.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, IConfiguration configuration, string schema)
        where TContext : DbContext
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"'ConnectionStrings:{ConnectionStringName}' yapılandırması eksik.");

        services.AddArmaPersistence();
        services.AddDbContext<TContext>((sp, options) => options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
            .UseSnakeCaseNamingConvention()
            // Sıra önemli: önce damgalar (silme yumuşak silmeye döner), sonra blind index, sonra denetim izi.
            .AddInterceptors(
                sp.GetRequiredService<TenantConnectionInterceptor>(),
                sp.GetRequiredService<EntityStampInterceptor>(),
                sp.GetRequiredService<BlindIndexInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>()));

        return services;
    }

    /// <summary>Tasarım zamanında (<c>dotnet ef</c>) kullanılan seçenekler.</summary>
    public static DbContextOptions<TContext> DesignTimeOptions<TContext>(string schema)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql("Host=localhost;Database=arma", npgsql => npgsql.MigrationsHistoryTable(MigrationsHistoryTable, schema))
            .UseSnakeCaseNamingConvention()
            .Options;

    private static void AddArmaPersistence(this IServiceCollection services)
    {
        if (services.Any(d => d.ServiceType == typeof(TenantConnectionInterceptor)))
        {
            return;
        }

        // Şifreleyici, ilk DbContext oluşturulurken (BlindIndexInterceptor aracılığıyla) kurulur ve
        // EF değer dönüştürücülerine atanır; anahtar eksikse uygulama ilk veritabanı erişiminde açık bir hatayla durur.
        services.AddSingleton(sp =>
        {
            var protector = SensitiveDataProtector.Create(
                sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>(), sp.GetRequiredService<ILoggerFactory>().CreateLogger<SensitiveDataProtector>());
            SensitiveDataProtector.UseForModel(protector);
            return protector;
        });
        services.AddSingleton<ISensitiveDataProtector>(sp => sp.GetRequiredService<SensitiveDataProtector>());
        services.AddScoped<BlindIndexInterceptor>();
        services.AddScoped<TenantConnectionInterceptor>();
        services.AddScoped<EntityStampInterceptor>();
        services.AddScoped<AuditInterceptor>();
    }
}
