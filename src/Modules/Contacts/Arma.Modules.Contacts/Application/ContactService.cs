using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Application;

public sealed record ContactListItem(Guid Id, ContactType Type, string DisplayName, string? Email, string? Phone, string? MaskedNationalId, string? City, IReadOnlyList<TagDto> Tags);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public sealed record ContactFilter(
    string? Search = null,
    ContactType? Type = null,
    Guid? TagId = null,
    Guid? GroupId = null,
    string? City = null,
    bool? HasEmail = null,
    bool? HasPhone = null,
    DateOnly? CreatedSince = null,
    IReadOnlyCollection<Guid>? Include = null,
    IReadOnlyCollection<Guid>? Exclude = null);

public sealed record CreatePersonRequest(string FirstName, string LastName, string? Email, string? Phone, string? Tckn);

public sealed record CreateOrganizationRequest(string Name, string? Email, string? Phone, string? Vkn);

public sealed record CreateHouseholdRequest(string Name, string? Email, string? Phone);

public sealed record UpdateContactRequest(
    string? FirstName,
    string? LastName,
    string? Name,
    string? Email,
    string? Phone,
    string? NationalId,
    DateOnly? BirthDate,
    string? Website,
    IReadOnlyList<Address>? Addresses);

public sealed record TagDto(Guid Id, string Name, string Color);

public sealed record GroupDto(Guid Id, string Name, string? Description, int MemberCount);

public sealed record RelationshipView(Guid Id, Guid OtherContactId, string OtherName, RelationshipType Type, string Label, string? Description, DateOnly? StartDate, DateOnly? EndDate);

public sealed record ContactDetail(
    Guid Id,
    ContactType Type,
    string? FirstName,
    string? LastName,
    string? OrganizationName,
    string DisplayName,
    string? Email,
    string? Phone,
    string? MaskedNationalId,
    DateOnly? BirthDate,
    string? Website,
    IReadOnlyList<Address> Addresses,
    IReadOnlyList<TagDto> Tags,
    IReadOnlyList<GroupDto> Groups,
    IReadOnlyList<RelationshipView> Relationships,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record DuplicateCandidate(Guid Id, string DisplayName, string? Email, string? Phone, string Reason);

public sealed record CreateRelationshipRequest(Guid OtherContactId, RelationshipType Type, bool ThisIsFrom, string? Description, DateOnly? StartDate);

public sealed class ContactService(ContactsDbContext db, IAuditLog audit, TimeProvider clock, IDbContextOutbox outbox) : IContactDirectory, IContactRegistry
{
    public const int MaxPageSize = 100;

    public async Task<PagedResult<ContactListItem>> ListAsync(ContactFilter filter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = Filter(filter);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .Include(c => c.Tags)
            .OrderBy(c => c.DisplayName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        return new PagedResult<ContactListItem>(rows.Select(ToListItem).ToList(), total, page, pageSize);
    }

    /// <summary>Eski imza: yalnızca arama metniyle listeleme.</summary>
    public Task<PagedResult<ContactListItem>> ListAsync(string? search, int page, int pageSize, CancellationToken cancellationToken = default) =>
        ListAsync(new ContactFilter(search), page, pageSize, cancellationToken);

    internal IQueryable<Contact> Filter(ContactFilter filter)
    {
        var query = db.Contacts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = Contact.Normalize(filter.Search);
            var digits = Contact.PhoneDigits(filter.Search);
            query = digits.Length >= 4
                ? query.Where(c => c.SearchText.Contains(term) || c.SearchText.Contains(digits))
                : query.Where(c => c.SearchText.Contains(term));
        }

        if (filter.Type is { } type)
        {
            query = query.Where(c => c.Type == type);
        }

        if (filter.TagId is { } tagId)
        {
            query = query.Where(c => c.Tags.Any(t => t.Id == tagId));
        }

        if (filter.GroupId is { } groupId)
        {
            query = query.Where(c => c.Groups.Any(g => g.Id == groupId));
        }

        if (!string.IsNullOrWhiteSpace(filter.City))
        {
            var city = filter.City.Trim();
            query = query.Where(c => c.Addresses.Any(a => a.City == city));
        }

        if (filter.HasEmail is { } hasEmail)
        {
            query = hasEmail ? query.Where(c => c.Email != null) : query.Where(c => c.Email == null);
        }

        if (filter.HasPhone is { } hasPhone)
        {
            query = hasPhone ? query.Where(c => c.Phone != null) : query.Where(c => c.Phone == null);
        }

        if (filter.CreatedSince is { } since)
        {
            var from = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            query = query.Where(c => c.CreatedAt >= from);
        }

        if (filter.Include is { } include)
        {
            query = query.Where(c => include.Contains(c.Id));
        }

        if (filter.Exclude is { Count: > 0 } exclude)
        {
            query = query.Where(c => !exclude.Contains(c.Id));
        }

        return query;
    }

    public async Task<ContactDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contact = await db.Contacts.AsNoTracking()
            .Include(c => c.Tags)
            .Include(c => c.Groups)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return null;
        }

        var relationships = await db.Relationships.AsNoTracking()
            .Where(r => r.FromContactId == id || r.ToContactId == id)
            .ToListAsync(cancellationToken);
        var otherIds = relationships.Select(r => r.FromContactId == id ? r.ToContactId : r.FromContactId).Distinct().ToList();
        var names = await db.Contacts.AsNoTracking().Where(c => otherIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.DisplayName, cancellationToken);

        var views = relationships
            .Select(r =>
            {
                var isFrom = r.FromContactId == id;
                var other = isFrom ? r.ToContactId : r.FromContactId;
                return new RelationshipView(r.Id, other, names.GetValueOrDefault(other, "(silinmiş kayıt)"), r.Type, Relationship.Label(r.Type, isFrom), r.Description, r.StartDate, r.EndDate);
            })
            .OrderBy(r => r.EndDate is not null)
            .ThenBy(r => r.OtherName)
            .ToList();

        return new ContactDetail(
            contact.Id, contact.Type, contact.FirstName, contact.LastName, contact.OrganizationName, contact.DisplayName,
            contact.Email, contact.Phone, contact.MaskedNationalId, contact.BirthDate, contact.Website, contact.Addresses,
            contact.Tags.OrderBy(t => t.Name).Select(ToDto).ToList(),
            contact.Groups.OrderBy(g => g.Name).Select(g => new GroupDto(g.Id, g.Name, g.Description, 0)).ToList(),
            views, contact.CreatedAt, contact.UpdatedAt);
    }

    /// <summary>Maskesiz kimlik numarası; her görüntüleme denetim izine yazılır.</summary>
    public async Task<string?> RevealNationalIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var value = await db.Contacts.AsNoTracking().Where(c => c.Id == id).Select(c => c.NationalId).FirstOrDefaultAsync(cancellationToken);
        if (value is not null)
        {
            await audit.WriteAsync(AuditAction.Viewed, nameof(Contact), id.ToString(), "Kimlik numarası maskesiz görüntülendi", cancellationToken);
        }

        return value;
    }

    /// <summary>Düzenleme formu için; denetim izine yazılmaz çünkü değer ekranda gösterilmez.</summary>
    internal Task<string?> CurrentNationalIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Contacts.AsNoTracking().Where(c => c.Id == id).Select(c => c.NationalId).FirstOrDefaultAsync(cancellationToken);

    public Task<Result<Guid>> CreatePersonAsync(CreatePersonRequest request, CancellationToken cancellationToken = default) =>
        AddAsync(Contact.CreatePerson(request.FirstName, request.LastName, request.Email, request.Phone, request.Tckn), cancellationToken);

    public Task<Result<Guid>> CreateOrganizationAsync(CreateOrganizationRequest request, CancellationToken cancellationToken = default) =>
        AddAsync(Contact.CreateOrganization(request.Name, request.Email, request.Phone, request.Vkn), cancellationToken);

    public Task<Result<Guid>> CreateHouseholdAsync(CreateHouseholdRequest request, CancellationToken cancellationToken = default) =>
        AddAsync(Contact.CreateHousehold(request.Name, request.Email, request.Phone), cancellationToken);

    public async Task<Result<Guid>> UpdateAsync(Guid id, UpdateContactRequest request, CancellationToken cancellationToken = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return ContactErrors.NotFound;
        }

        var updated = contact.Type == ContactType.Person
            ? contact.UpdatePerson(request.FirstName ?? "", request.LastName ?? "", request.Email, request.Phone, request.NationalId, request.BirthDate)
            : contact.UpdateOrganization(request.Name ?? "", request.Email, request.Phone, request.NationalId, request.Website);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        if (request.Addresses is not null)
        {
            contact.SetAddresses(request.Addresses);
        }

        if (contact.NationalId is not null && await NationalIdTakenAsync(contact.NationalId, contact.Id, cancellationToken))
        {
            return ContactErrors.DuplicateNationalId;
        }

        await db.SaveChangesAsync(cancellationToken);
        return contact.Id;
    }

    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return ContactErrors.NotFound;
        }

        db.Contacts.Remove(contact);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>Aynı kimlik numarası, e-posta, telefon veya adla kayıtlı olası mükerrerler.</summary>
    public async Task<IReadOnlyList<DuplicateCandidate>> FindDuplicatesAsync(string? displayName, string? email, string? phone, string? nationalId, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        nationalId = string.IsNullOrWhiteSpace(nationalId) ? null : nationalId.Trim();
        var digits = Contact.PhoneDigits(phone);
        var name = string.IsNullOrWhiteSpace(displayName) ? null : Contact.Normalize(displayName);

        var candidates = await db.Contacts.AsNoTracking()
            .Where(c => c.Id != excludeId)
            .Where(c => (nationalId != null && c.NationalId == nationalId)
                        || (email != null && c.Email == email)
                        || (digits.Length >= 7 && c.SearchText.Contains(digits))
                        || (name != null && c.SearchText.StartsWith(name + " ")))
            .Take(10)
            .ToListAsync(cancellationToken);

        return candidates.Select(c => new DuplicateCandidate(c.Id, c.DisplayName, c.Email, c.Phone,
                nationalId != null && c.NationalId == nationalId ? "Aynı kimlik numarası"
                : email != null && c.Email == email ? "Aynı e-posta"
                : digits.Length >= 7 && Contact.PhoneDigits(c.Phone) == digits ? "Aynı telefon"
                : "Benzer ad"))
            .ToList();
    }

    /// <summary>
    /// <paramref name="duplicateId"/> kaydını <paramref name="primaryId"/> kaydına birleştirir: boş alanlar,
    /// etiketler, gruplar, adresler ve ilişkiler asıl kayda geçer; mükerrer kayıt silinir.
    /// </summary>
    public async Task<Result<Guid>> MergeAsync(Guid primaryId, Guid duplicateId, CancellationToken cancellationToken = default)
    {
        var contacts = await db.Contacts.Include(c => c.Tags).Include(c => c.Groups).AsSplitQuery()
            .Where(c => c.Id == primaryId || c.Id == duplicateId)
            .ToListAsync(cancellationToken);
        var primary = contacts.FirstOrDefault(c => c.Id == primaryId);
        var duplicate = contacts.FirstOrDefault(c => c.Id == duplicateId);
        if (primary is null || duplicate is null)
        {
            return ContactErrors.NotFound;
        }

        var nationalId = duplicate.NationalId;
        var merged = primary.Absorb(duplicate);
        if (!merged.IsSuccess)
        {
            return merged.Error!;
        }

        // Benzersiz indeks çakışmasın diye mükerrer kaydın kimlik numarası önce boşaltılır.
        if (nationalId is not null)
        {
            db.Entry(duplicate).Property(c => c.NationalId).CurrentValue = null;
        }

        var relationships = await db.Relationships.Where(r => r.FromContactId == duplicateId || r.ToContactId == duplicateId).ToListAsync(cancellationToken);
        foreach (var relationship in relationships)
        {
            relationship.Repoint(duplicateId, primaryId);
            if (relationship.FromContactId == relationship.ToContactId)
            {
                db.Relationships.Remove(relationship);
            }
        }

        // Olay, birleştirmeyle aynı işlemde kaydedilir; diğer modüller kendi verilerini asıl kayda taşır.
        outbox.Enroll(db);
        await outbox.PublishAsync(new ContactsMerged(primaryId, duplicateId));
        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        await audit.WriteAsync(AuditAction.Merged, nameof(Contact), primaryId.ToString(), $"{duplicate.DisplayName} ({duplicateId}) bu kayda birleştirildi", cancellationToken);
        return primaryId;
    }

    public async Task<Result<Guid>> AddRelationshipAsync(Guid contactId, CreateRelationshipRequest request, CancellationToken cancellationToken = default)
    {
        var ids = new[] { contactId, request.OtherContactId };
        if (await db.Contacts.CountAsync(c => ids.Contains(c.Id), cancellationToken) != 2)
        {
            return ContactErrors.NotFound;
        }

        var (from, to) = request.ThisIsFrom ? (contactId, request.OtherContactId) : (request.OtherContactId, contactId);
        var created = Relationship.Create(from, to, request.Type, request.Description, request.StartDate);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        db.Relationships.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value!.Id;
    }

    public async Task<bool> EndRelationshipAsync(Guid relationshipId, CancellationToken cancellationToken = default)
    {
        var relationship = await db.Relationships.FirstOrDefaultAsync(r => r.Id == relationshipId, cancellationToken);
        if (relationship is null)
        {
            return false;
        }

        relationship.End(DateOnly.FromDateTime(clock.GetLocalNow().DateTime));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RemoveRelationshipAsync(Guid relationshipId, CancellationToken cancellationToken = default)
    {
        var relationship = await db.Relationships.FirstOrDefaultAsync(r => r.Id == relationshipId, cancellationToken);
        if (relationship is null)
        {
            return false;
        }

        db.Relationships.Remove(relationship);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ContactSummary?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Birleştirilmiş kayıtlar silinmiş görünür; zinciri izleyip asıl kaydı bul.
        for (var hops = 0; hops < 5; hops++)
        {
            var contact = await db.Contacts.IgnoreQueryFilters().AsNoTracking()
                .Where(c => c.TenantId == db.TenantId && c.Id == id)
                .FirstOrDefaultAsync(cancellationToken);
            if (contact is null)
            {
                return null;
            }

            if (contact.MergedIntoId is { } next)
            {
                id = next;
                continue;
            }

            return contact.IsDeleted ? null : contact.ToSummary();
        }

        return null;
    }

    public async Task<ContactRegistration> FindOrCreatePersonAsync(string firstName, string lastName, string? email, string? phone, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalized = email.Trim().ToLowerInvariant();
            var existing = await db.Contacts.AsNoTracking()
                .Where(c => c.Type == ContactType.Person && c.Email != null && c.Email.ToLower() == normalized)
                .OrderBy(c => c.CreatedAt)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing is not null)
            {
                return new ContactRegistration(existing, false, null);
            }
        }

        var created = await CreatePersonAsync(new CreatePersonRequest(firstName, lastName, email, phone, null), cancellationToken);
        return created.IsSuccess ? new ContactRegistration(created.Value, true, null) : new ContactRegistration(null, false, created.Error!.Message);
    }

    public async Task<IReadOnlyDictionary<Guid, ContactSummary>> FindManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
    {
        var rows = await db.Contacts.AsNoTracking().Where(c => ids.Contains(c.Id)).ToListAsync(cancellationToken);
        return rows.ToDictionary(c => c.Id, c => c.ToSummary());
    }

    public async Task<IReadOnlyList<ContactSummary>> SearchAsync(string term, int take = 10, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        var rows = await Filter(new ContactFilter(term)).OrderBy(c => c.DisplayName).Take(Math.Clamp(take, 1, 50)).ToListAsync(cancellationToken);
        return rows.Select(c => c.ToSummary()).ToList();
    }

    private async Task<Result<Guid>> AddAsync(Result<Contact> created, CancellationToken cancellationToken)
    {
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        if (created.Value!.NationalId is { } nationalId && await NationalIdTakenAsync(nationalId, null, cancellationToken))
        {
            return ContactErrors.DuplicateNationalId;
        }

        db.Contacts.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(created.Value!.Id);
    }

    private Task<bool> NationalIdTakenAsync(string nationalId, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Contacts.AnyAsync(c => c.NationalId == nationalId && c.Id != exceptId, cancellationToken);

    internal static TagDto ToDto(Tag tag) => new(tag.Id, tag.Name, tag.Color);

    private static ContactListItem ToListItem(Contact c) =>
        new(c.Id, c.Type, c.DisplayName, c.Email, c.Phone, c.MaskedNationalId, c.PrimaryAddress?.City, c.Tags.OrderBy(t => t.Name).Select(ToDto).ToList());
}
