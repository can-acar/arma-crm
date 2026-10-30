namespace Arma.SharedKernel.Domain;

/// <summary>
/// Tüm kalıcı varlıkların temeli. Kimlikler zaman sıralı (UUID v7) üretilir,
/// böylece indeks parçalanması azalır ve kayıtlar oluşturulma sırasıyla gelir.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public string? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }
}
