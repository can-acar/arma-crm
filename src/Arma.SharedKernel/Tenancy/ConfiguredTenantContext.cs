using Microsoft.Extensions.Configuration;

namespace Arma.SharedKernel.Tenancy;

public sealed class ConfiguredTenantContext(IConfiguration configuration) : ITenantContext
{
    public const string ConfigurationKey = "Arma:TenantId";

    public Guid TenantId { get; } = Guid.TryParse(configuration[ConfigurationKey], out var id)
        ? id
        : throw new InvalidOperationException($"'{ConfigurationKey}' yapılandırması eksik veya geçersiz.");
}
