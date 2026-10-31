using System.Data.Common;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Arma.Modules.Contacts.Infrastructure;

/// <summary>
/// Her bağlantıda <c>app.tenant_id</c> oturum değişkenini atar. PostgreSQL Row Level Security
/// politikaları bu değişkene bakar; EF sorgu filtresi unutulsa bile başka kurumun satırı dönmez.
/// </summary>
internal sealed class TenantConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenant.TenantId.ToString();
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenant.TenantId.ToString();
        command.Parameters.Add(parameter);
        command.ExecuteNonQuery();
    }
}
