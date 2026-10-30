using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Application;

public sealed record ContactListItem(Guid Id, ContactType Type, string DisplayName, string? Email, string? Phone, string? MaskedNationalId);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record CreatePersonRequest(string FirstName, string LastName, string? Email, string? Phone, string? Tckn);

public sealed record CreateOrganizationRequest(string Name, string? Email, string? Phone, string? Vkn);

public sealed class ContactService(ContactsDbContext db) : IContactDirectory
{
    public const int MaxPageSize = 100;

    public async Task<PagedResult<ContactListItem>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Contacts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = Contact.Normalize(search);
            query = query.Where(c => c.SearchText.Contains(term));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(c => c.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(c => new ContactListItem(c.Id, c.Type, c.DisplayName, c.Email, c.Phone, c.MaskedNationalId))
            .ToList();

        return new PagedResult<ContactListItem>(items, total, page, pageSize);
    }

    public Task<Result<Guid>> CreatePersonAsync(CreatePersonRequest request, CancellationToken cancellationToken = default) =>
        AddAsync(Contact.CreatePerson(request.FirstName, request.LastName, request.Email, request.Phone, request.Tckn), cancellationToken);

    public Task<Result<Guid>> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default) =>
        AddAsync(Contact.CreateOrganization(request.Name, request.Email, request.Phone, request.Vkn), cancellationToken);

    public async Task<ContactSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        return contact?.ToSummary();
    }

    private async Task<Result<Guid>> AddAsync(Result<Contact> created, CancellationToken cancellationToken)
    {
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        db.Contacts.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(created.Value!.Id);
    }
}
