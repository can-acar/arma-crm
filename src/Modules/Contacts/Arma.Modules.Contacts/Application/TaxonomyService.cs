using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Application;

/// <summary>Etiketler ve statik gruplar.</summary>
public sealed class TaxonomyService(ContactsDbContext db)
{
    public async Task<IReadOnlyList<TagDto>> TagsAsync(CancellationToken cancellationToken = default) =>
        await db.Tags.AsNoTracking().OrderBy(t => t.Name).Select(t => new TagDto(t.Id, t.Name, t.Color)).ToListAsync(cancellationToken);

    public async Task<Result<Guid>> CreateTagAsync(string name, string? color, CancellationToken cancellationToken = default)
    {
        var created = Tag.Create(name, color);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        if (await db.Tags.AnyAsync(t => t.Name == created.Value!.Name, cancellationToken))
        {
            return new Error("tags.duplicate", "Bu adla bir etiket zaten var.");
        }

        db.Tags.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value!.Id;
    }

    public async Task<bool> DeleteTagAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var tag = await db.Tags.Include(t => t.Contacts).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tag is null)
        {
            return false;
        }

        tag.Contacts.Clear();
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Kayda etiket ekler; etiket adı yoksa oluşturur.</summary>
    public async Task<Result<TagDto>> TagContactAsync(Guid contactId, string tagName, CancellationToken cancellationToken = default)
    {
        var contact = await db.Contacts.Include(c => c.Tags).FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null)
        {
            return ContactErrors.NotFound;
        }

        var name = tagName.Trim();
        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Name == name, cancellationToken);
        if (tag is null)
        {
            var created = Tag.Create(name, null);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            tag = created.Value!;
            db.Tags.Add(tag);
        }

        if (contact.Tags.All(t => t.Id != tag.Id))
        {
            contact.Tags.Add(tag);
            await db.SaveChangesAsync(cancellationToken);
        }

        return ContactService.ToDto(tag);
    }

    public async Task<bool> UntagContactAsync(Guid contactId, Guid tagId, CancellationToken cancellationToken = default)
    {
        var contact = await db.Contacts.Include(c => c.Tags).FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        var tag = contact?.Tags.FirstOrDefault(t => t.Id == tagId);
        if (tag is null)
        {
            return false;
        }

        contact!.Tags.Remove(tag);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<GroupDto>> GroupsAsync(CancellationToken cancellationToken = default) =>
        await db.Groups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GroupDto(g.Id, g.Name, g.Description, g.Members.Count(m => !m.IsDeleted)))
            .ToListAsync(cancellationToken);

    public async Task<Result<Guid>> CreateGroupAsync(string name, string? description, CancellationToken cancellationToken = default)
    {
        var created = Group.Create(name, description);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        if (await db.Groups.AnyAsync(g => g.Name == created.Value!.Name, cancellationToken))
        {
            return new Error("groups.duplicate", "Bu adla bir grup zaten var.");
        }

        db.Groups.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value!.Id;
    }

    public async Task<bool> DeleteGroupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (group is null)
        {
            return false;
        }

        group.Members.Clear();
        db.Groups.Remove(group);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Result<int>> AddMembersAsync(Guid groupId, IReadOnlyCollection<Guid> contactIds, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        if (group is null)
        {
            return new Error("groups.not_found", "Grup bulunamadı.");
        }

        var existing = group.Members.Select(m => m.Id).ToHashSet();
        var toAdd = await db.Contacts.Where(c => contactIds.Contains(c.Id) && !existing.Contains(c.Id)).ToListAsync(cancellationToken);
        group.Members.AddRange(toAdd);
        await db.SaveChangesAsync(cancellationToken);
        return toAdd.Count;
    }

    public async Task<bool> RemoveMemberAsync(Guid groupId, Guid contactId, CancellationToken cancellationToken = default)
    {
        var group = await db.Groups.Include(g => g.Members).FirstOrDefaultAsync(g => g.Id == groupId, cancellationToken);
        var member = group?.Members.FirstOrDefault(m => m.Id == contactId);
        if (member is null)
        {
            return false;
        }

        group!.Members.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
