namespace Arma.Modules.Contacts.Contracts;

/// <summary>Kaydın oluşturulması ya da bulunması sonucu; <see cref="Error"/> doluysa kayıt yapılamadı.</summary>
public sealed record ContactRegistration(Guid? Id, bool Created, string? Error);

/// <summary>
/// Diğer modüllerin dışarıdan gelen kişileri (çevrim içi bağış formu gibi) kaydettiği arayüz.
/// Aynı e-postayla bir kişi varsa o kayıt kullanılır, yoksa yeni kişi açılır.
/// </summary>
public interface IContactRegistry
{
    Task<ContactRegistration> FindOrCreatePersonAsync(string firstName, string lastName, string? email, string? phone, CancellationToken cancellationToken = default);

    /// <summary>
    /// Eski sistemden aktarılmış kişilerin dış anahtarlarını ("civicrm:42") Arma kimliklerine çevirir. Birleştirilmiş
    /// kayıtlar asıl kayda çözülür; bulunamayan anahtarlar sonuçta yer almaz.
    /// </summary>
    Task<IReadOnlyDictionary<string, Guid>> FindByExternalKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);
}
