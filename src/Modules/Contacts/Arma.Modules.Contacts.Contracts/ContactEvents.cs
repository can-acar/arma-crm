namespace Arma.Modules.Contacts.Contracts;

/// <summary>
/// Mükerrer kayıt asıl kayda birleştirildi. Kişiye bağlı veri tutan modüller (aktiviteler, bağışlar)
/// kendi kayıtlarını <see cref="PrimaryId"/> kaydına taşır.
/// </summary>
public sealed record ContactsMerged(Guid PrimaryId, Guid DuplicateId);
