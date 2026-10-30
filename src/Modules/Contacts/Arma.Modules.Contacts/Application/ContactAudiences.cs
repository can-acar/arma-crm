using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Application;

internal sealed class ContactAudiences(ContactsDbContext db) : IContactAudiences
{
    public async Task<IReadOnlyList<AudienceList>> GroupsAsync(CancellationToken cancellationToken = default) =>
        await db.Groups.AsNoTracking().OrderBy(g => g.Name)
            .Select(g => new AudienceList(g.Id, g.Name, g.Members.Count(m => !m.IsDeleted)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AudienceList>> TagsAsync(CancellationToken cancellationToken = default) =>
        await db.Tags.AsNoTracking().OrderBy(t => t.Name)
            .Select(t => new AudienceList(t.Id, t.Name, t.Contacts.Count(c => !c.IsDeleted)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AudienceMember>> MembersAsync(Guid? groupId, Guid? tagId, CancellationToken cancellationToken = default)
    {
        var query = db.Contacts.AsNoTracking();
        if (groupId is { } g)
        {
            query = query.Where(c => c.Groups.Any(x => x.Id == g));
        }

        if (tagId is { } t)
        {
            query = query.Where(c => c.Tags.Any(x => x.Id == t));
        }

        return await query.OrderBy(c => c.DisplayName)
            .Select(c => new AudienceMember(c.Id, c.Type, c.DisplayName, c.FirstName, c.LastName, c.Email, c.Phone))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, AudienceMember>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
        await db.Contacts.AsNoTracking().Where(c => ids.Contains(c.Id))
            .Select(c => new AudienceMember(c.Id, c.Type, c.DisplayName, c.FirstName, c.LastName, c.Email, c.Phone))
            .ToDictionaryAsync(c => c.Id, cancellationToken);
}
