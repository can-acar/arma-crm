using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Arma.IntegrationTests;

/// <summary>
/// Uygulamayı gerçek bir PostgreSQL veritabanıyla başlatır. Her test sınıfı için geçici bir veritabanı
/// oluşturulur ve sonunda silinir. Bağlantı <c>ARMA_TEST_CONNECTION</c> ortam değişkeninden gelir
/// (veritabanı oluşturma yetkisi olan bir kullanıcı); tanımlı değilse bu testler atlanır.
/// </summary>
public sealed class ArmaAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string ConnectionVariable = "ARMA_TEST_CONNECTION";

    public static readonly Guid TenantId = Guid.Parse("0193a6f0-0000-7000-8000-0000000000aa");

    private static readonly string? ServerConnection = Environment.GetEnvironmentVariable(ConnectionVariable);

    private readonly string databaseName = "arma_test_" + Guid.NewGuid().ToString("N")[..12];

    public static bool IsConfigured => !string.IsNullOrEmpty(ServerConnection);

    public string ConnectionString => new NpgsqlConnectionStringBuilder(ServerConnection) { Database = databaseName }.ConnectionString;

    public async ValueTask InitializeAsync()
    {
        if (!IsConfigured)
        {
            return;
        }

        await using var connection = new NpgsqlConnection(ServerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);
        await command.ExecuteNonQueryAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:arma", ConnectionString);
        builder.UseSetting("Arma:TenantId", TenantId.ToString());
        builder.UseSetting("Arma:MigrateOnStartup", "true");
        builder.UseSetting("Arma:Security:AllowHttp", "true");
    }

    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (!IsConfigured)
        {
            return;
        }

        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(ServerConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS {databaseName} WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>Veritabanı yapılandırılmamışsa testi atlar.</summary>
internal static class RequiresDatabase
{
    public static void Check() =>
        Assert.SkipUnless(ArmaAppFactory.IsConfigured, $"{ArmaAppFactory.ConnectionVariable} tanımlı değil; PostgreSQL gerektiren test atlandı.");
}
