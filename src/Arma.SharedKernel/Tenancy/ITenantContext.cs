namespace Arma.SharedKernel.Tenancy;

/// <summary>
/// Geçerli isteğin kurumunu verir. İlk sürümde her kurulum tek kurumdur;
/// değer yapılandırmadan gelir. Bulut sürümünde istekten çözülecektir.
/// </summary>
public interface ITenantContext
{
    Guid TenantId { get; }
}
