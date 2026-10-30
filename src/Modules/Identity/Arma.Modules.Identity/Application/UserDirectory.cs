using Arma.Modules.Identity.Contracts;
using Arma.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Identity.Application;

internal sealed class UserDirectory(ArmaIdentityDbContext db) : IUserDirectory
{
    public async Task<IReadOnlyList<UserSummary>> ActiveUsersAsync(CancellationToken cancellationToken = default) =>
        await db.Users.AsNoTracking()
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new UserSummary(u.Id, u.FullName, u.Email, u.IsActive))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, UserSummary>();
        }

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.FullName, u.Email, u.IsActive))
            .ToDictionaryAsync(u => u.Id, cancellationToken);
    }
}
