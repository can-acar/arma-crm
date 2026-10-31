namespace Arma.Modules.Contacts.Contracts;

/// <summary>
/// Kişiler modülünün dışarıya açtığı okuma arayüzü. Diğer modüller kişi tablosuna
/// doğrudan erişmez; yalnızca bu arayüzü kullanır.
/// </summary>
public interface IContactDirectory
{
    /// <summary>Kaydı getirir. Kayıt başka bir kayıtla birleştirildiyse asıl kaydı döner.</summary>
    Task<ContactSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, ContactSummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>E-posta adresi kayıtlı ilk kişi (bağışçı portalı girişi gibi); büyük/küçük harf ayrımı yapılmaz.</summary>
    Task<ContactSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Kişi seçici için ad, e-posta veya telefonla hızlı arama.</summary>
    Task<IReadOnlyList<ContactSummary>> SearchAsync(string term, int take = 10, CancellationToken cancellationToken = default);
}
