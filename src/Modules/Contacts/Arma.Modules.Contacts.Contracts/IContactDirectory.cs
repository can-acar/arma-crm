namespace Arma.Modules.Contacts.Contracts;

/// <summary>
/// Kişiler modülünün dışarıya açtığı okuma arayüzü. Diğer modüller kişi tablosuna
/// doğrudan erişmez; yalnızca bu arayüzü kullanır.
/// </summary>
public interface IContactDirectory
{
    Task<ContactSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default);
}
