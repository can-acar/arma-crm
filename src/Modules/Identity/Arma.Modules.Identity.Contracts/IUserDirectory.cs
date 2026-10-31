namespace Arma.Modules.Identity.Contracts;

public sealed record UserSummary(Guid Id, string FullName, string? Email, bool IsActive);

/// <summary>
/// Kimlik modülünün dışarıya açtığı kullanıcı listesi. Diğer modüller kullanıcı tablosuna
/// doğrudan erişmez; görev atama gibi işler için bu arayüzü kullanır.
/// </summary>
public interface IUserDirectory
{
    /// <summary>Oturum açabilen (etkin) kullanıcılar, ada göre sıralı.</summary>
    Task<IReadOnlyList<UserSummary>> ActiveUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Pasif kullanıcılar da dahil, verilen kimliklerin karşılıkları.</summary>
    Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
