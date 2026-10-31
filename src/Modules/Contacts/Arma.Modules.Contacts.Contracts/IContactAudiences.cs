namespace Arma.Modules.Contacts.Contracts;

public sealed record AudienceList(Guid Id, string Name, int Count);

/// <summary>Toplu ileti ve kişiselleştirme için gereken kişi bilgileri.</summary>
public sealed record AudienceMember(Guid Id, ContactType Type, string DisplayName, string? FirstName, string? LastName, string? Email, string? Phone);

/// <summary>Diğer modüllerin (toplu e-posta, SMS) kişi gruplarına ve etiketlere eriştiği arayüz.</summary>
public interface IContactAudiences
{
    Task<IReadOnlyList<AudienceList>> GroupsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AudienceList>> TagsAsync(CancellationToken cancellationToken = default);

    /// <summary>Gruptaki ve/veya etiketteki kişiler; ikisi de boşsa tüm kişiler.</summary>
    Task<IReadOnlyList<AudienceMember>> MembersAsync(Guid? groupId, Guid? tagId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, AudienceMember>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);
}
