using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Privacy.Domain;
using Arma.Modules.Privacy.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Privacy.Application;

public sealed record DataRequestView(
    Guid Id, RequestType Type, RequestChannel Channel, string RequesterName, string? Email, string? Phone, string Description,
    Guid? ContactId, string? ContactName, DateOnly ReceivedOn, DateOnly DueOn, RequestStatus Status, bool IdentityVerified,
    string? Response, DateTimeOffset? ClosedAt, bool IsOverdue, int DaysLeft);

public sealed record CreateDataRequest(RequestType Type, RequestChannel Channel, string Name, string? Email, string? Phone, string Description, DateOnly? ReceivedOn = null);

/// <summary>İlgili kişi başvuruları (KVKK m.11, m.13).</summary>
public sealed class DataRequestService(PrivacyDbContext db, IContactDirectory contacts, TimeProvider clock)
{
    public async Task<IReadOnlyList<DataRequestView>> ListAsync(bool openOnly = false, Guid? contactId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Requests.AsNoTracking();
        if (openOnly)
        {
            query = query.Where(r => r.Status == RequestStatus.New || r.Status == RequestStatus.InProgress);
        }

        if (contactId is { } id)
        {
            query = query.Where(r => r.ContactId == id);
        }

        var rows = await query.OrderBy(r => r.Status == RequestStatus.New || r.Status == RequestStatus.InProgress ? 0 : 1)
            .ThenBy(r => r.DueOn).Take(500).ToListAsync(cancellationToken);
        return await ToViewsAsync(rows, cancellationToken);
    }

    public async Task<DataRequestView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await db.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        return row is null ? null : (await ToViewsAsync([row], cancellationToken))[0];
    }

    public async Task<int> OverdueCountAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        return await db.Requests.CountAsync(r => (r.Status == RequestStatus.New || r.Status == RequestStatus.InProgress) && r.DueOn < today, cancellationToken);
    }

    public async Task<Result<Guid>> CreateAsync(CreateDataRequest request, CancellationToken cancellationToken = default)
    {
        var created = DataRequest.Create(request.Type, request.Channel, request.Name, request.Email, request.Phone, request.Description, request.ReceivedOn ?? Today);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        db.Requests.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value!.Id;
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, Guid? contactId, bool identityVerified, CancellationToken cancellationToken = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return PrivacyErrors.RequestNotFound;
        }

        if (contactId is { } cid && await contacts.FindAsync(cid, cancellationToken) is null)
        {
            return PrivacyErrors.ContactNotFound;
        }

        request.LinkContact(contactId);
        request.SetIdentityVerified(identityVerified);
        if (request.Status == RequestStatus.New)
        {
            request.Start();
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> CloseAsync(Guid id, bool completed, string? response, CancellationToken cancellationToken = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return PrivacyErrors.RequestNotFound;
        }

        var closed = request.Close(completed, response, clock.GetUtcNow());
        if (!closed.IsSuccess)
        {
            return closed.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var request in await db.Requests.Where(r => r.ContactId == from).ToListAsync(cancellationToken))
        {
            request.LinkContact(to);
        }

        await db.SaveChangesAsync(cancellationToken);

        // Rıza kayıtları değiştirilemez; birleştirmede yalnızca kişi bağı, bu işleme özel izinle taşınır.
        await db.Database.ExecuteSqlRawAsync("SELECT set_config('arma.allow_privacy_change', 'on', true)", cancellationToken);
        await db.ConsentRecords.Where(c => c.ContactId == from)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ContactId, to), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private async Task<IReadOnlyList<DataRequestView>> ToViewsAsync(IReadOnlyList<DataRequest> rows, CancellationToken cancellationToken)
    {
        var names = await contacts.FindManyAsync([.. rows.Select(r => r.ContactId).OfType<Guid>().Distinct()], cancellationToken);

        var today = Today;
        return rows.Select(r => new DataRequestView(r.Id, r.Type, r.Channel, r.RequesterName, r.Email, r.Phone, r.Description,
                r.ContactId, r.ContactId is { } c && names.TryGetValue(c, out var n) ? n.DisplayName : null, r.ReceivedOn, r.DueOn, r.Status, r.IdentityVerified,
                r.Response, r.ClosedAt, r.IsOverdue(today), r.DueOn.DayNumber - today.DayNumber))
            .ToList();
    }
}
