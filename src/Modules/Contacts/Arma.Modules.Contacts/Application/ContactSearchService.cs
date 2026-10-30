using System.Globalization;
using System.Text.Json;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Application;

public sealed record SearchCriterion(string Key, string? Value);

public sealed record SavedSearchView(Guid Id, string Name, IReadOnlyList<SearchCriterion> Criteria);

/// <summary>
/// Gelişmiş kişi araması: kişi alanlarındaki ölçütler burada, bağış, izin, aktivite gibi ölçütler sahibi olan
/// modülde çalışır (<see cref="IContactCriteriaProvider"/>). Tüm ölçütler "ve" ile birleşir.
/// </summary>
public sealed class ContactSearchService(
    ContactsDbContext db,
    ContactService contacts,
    ContactTransferService transfer,
    TaxonomyService taxonomy,
    IEnumerable<IContactCriteriaProvider> providers) : IContactSegments
{
    private const string Group = "Kişi";

    public async Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var tags = await db.Tags.AsNoTracking().OrderBy(t => t.Name).Select(t => new CriterionOption(t.Id.ToString(), t.Name)).ToListAsync(cancellationToken);
        var groups = await db.Groups.AsNoTracking().OrderBy(g => g.Name).Select(g => new CriterionOption(g.Id.ToString(), g.Name)).ToListAsync(cancellationToken);
        var list = new List<CriterionDefinition>
        {
            new("search", Group, "Ad, e-posta veya telefon içerir", CriterionKind.Text),
            new("type", Group, "Kayıt türü", CriterionKind.Choice, [new("Person", "Kişi"), new("Organization", "Kurum"), new("Household", "Hane")]),
            new("tag", Group, "Etiketi var", CriterionKind.Choice, tags),
            new("no_tag", Group, "Etiketi yok", CriterionKind.Choice, tags),
            new("group", Group, "Grubun üyesi", CriterionKind.Choice, groups),
            new("city", Group, "İl", CriterionKind.Text),
            new("has_email", Group, "E-postası var", CriterionKind.Flag),
            new("no_email", Group, "E-postası yok", CriterionKind.Flag),
            new("has_phone", Group, "Telefonu var", CriterionKind.Flag),
            new("created_since", Group, "Bu tarihten sonra eklendi", CriterionKind.Date),
        };
        foreach (var provider in providers)
        {
            list.AddRange(await provider.DefinitionsAsync(cancellationToken));
        }

        return list;
    }

    public async Task<Result<ContactFilter>> BuildFilterAsync(IReadOnlyList<SearchCriterion> criteria, CancellationToken cancellationToken = default)
    {
        var filter = new ContactFilter();
        HashSet<Guid>? include = null;
        var exclude = new HashSet<Guid>();

        void Intersect(IEnumerable<Guid> ids)
        {
            if (include is null)
            {
                include = [.. ids];
            }
            else
            {
                include.IntersectWith(ids);
            }
        }

        foreach (var criterion in criteria)
        {
            var value = criterion.Value?.Trim();
            switch (criterion.Key)
            {
                case "search":
                    filter = filter with { Search = value };
                    break;
                case "type" when Enum.TryParse<ContactType>(value, out var type):
                    filter = filter with { Type = type };
                    break;
                case "tag" when Guid.TryParse(value, out var tag):
                    Intersect(await db.Contacts.Where(c => c.Tags.Any(t => t.Id == tag)).Select(c => c.Id).ToListAsync(cancellationToken));
                    break;
                case "no_tag" when Guid.TryParse(value, out var tag):
                    exclude.UnionWith(await db.Contacts.Where(c => c.Tags.Any(t => t.Id == tag)).Select(c => c.Id).ToListAsync(cancellationToken));
                    break;
                case "group" when Guid.TryParse(value, out var group):
                    Intersect(await db.Contacts.Where(c => c.Groups.Any(g => g.Id == group)).Select(c => c.Id).ToListAsync(cancellationToken));
                    break;
                case "city":
                    filter = filter with { City = value };
                    break;
                case "has_email":
                    filter = filter with { HasEmail = true };
                    break;
                case "no_email":
                    filter = filter with { HasEmail = false };
                    break;
                case "has_phone":
                    filter = filter with { HasPhone = true };
                    break;
                case "created_since" when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var since):
                    filter = filter with { CreatedSince = since };
                    break;
                case "tag" or "no_tag" or "group" or "type" or "created_since":
                    return new Error("search.invalid_value", "Bir ölçütün değeri eksik veya geçersiz.");
                default:
                    var match = await MatchAsync(criterion.Key, value, cancellationToken);
                    if (match is null)
                    {
                        return new Error("search.invalid_value", "Bir ölçütün değeri eksik veya geçersiz.");
                    }

                    if (match.Exclude)
                    {
                        exclude.UnionWith(match.ContactIds);
                    }
                    else
                    {
                        Intersect(match.ContactIds);
                    }

                    break;
            }
        }

        return filter with { Include = include, Exclude = exclude };
    }

    public async Task<Result<PagedResult<ContactListItem>>> SearchAsync(IReadOnlyList<SearchCriterion> criteria, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var filter = await BuildFilterAsync(criteria, cancellationToken);
        return filter.IsSuccess ? await contacts.ListAsync(filter.Value!, page, pageSize, cancellationToken) : filter.Error!;
    }

    public async Task<Result<byte[]>> ExportAsync(IReadOnlyList<SearchCriterion> criteria, CancellationToken cancellationToken = default)
    {
        var filter = await BuildFilterAsync(criteria, cancellationToken);
        return filter.IsSuccess ? await transfer.ExportAsync(filter.Value!, false, cancellationToken) : filter.Error!;
    }

    /// <summary>Sonuçtaki herkesi bir gruba ekler; grup toplu iletide hedef kitle olarak seçilebilir.</summary>
    public async Task<Result<int>> AddToGroupAsync(IReadOnlyList<SearchCriterion> criteria, Guid groupId, CancellationToken cancellationToken = default)
    {
        var filter = await BuildFilterAsync(criteria, cancellationToken);
        if (!filter.IsSuccess)
        {
            return filter.Error!;
        }

        var ids = await contacts.Filter(filter.Value!).Select(c => c.Id).ToListAsync(cancellationToken);
        return await taxonomy.AddMembersAsync(groupId, ids, cancellationToken);
    }

    public async Task<IReadOnlyList<SavedSearchView>> SavedAsync(CancellationToken cancellationToken = default) =>
        (await db.SavedSearches.AsNoTracking().OrderBy(s => s.Name).ToListAsync(cancellationToken)).Select(ToView).ToList();

    public async Task<Result<Guid>> SaveAsync(Guid? id, string name, IReadOnlyList<SearchCriterion> criteria, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(criteria);
        SavedSearch? search;
        if (id is null)
        {
            var created = SavedSearch.Create(name, json);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            search = created.Value!;
            db.SavedSearches.Add(search);
        }
        else
        {
            search = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (search is null)
            {
                return new Error("searches.not_found", "Kayıtlı arama bulunamadı.");
            }

            var updated = search.Update(name, json);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }
        }

        if (await db.SavedSearches.AnyAsync(s => s.Name == search.Name && s.Id != search.Id, cancellationToken))
        {
            return new Error("searches.name_taken", "Bu adla kayıtlı bir arama var.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return search.Id;
    }

    public async Task<bool> DeleteSavedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var search = await db.SavedSearches.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (search is null)
        {
            return false;
        }

        db.SavedSearches.Remove(search);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken)
    {
        foreach (var provider in providers)
        {
            if (await provider.MatchAsync(key, value, cancellationToken) is { } match)
            {
                return match;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<SegmentInfo>> SegmentsAsync(CancellationToken cancellationToken = default) =>
        await db.SavedSearches.AsNoTracking().OrderBy(s => s.Name).Select(s => new SegmentInfo(s.Id, s.Name)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>?> MatchAsync(Guid segmentId, CancellationToken cancellationToken = default)
    {
        var search = await db.SavedSearches.AsNoTracking().FirstOrDefaultAsync(s => s.Id == segmentId, cancellationToken);
        if (search is null)
        {
            return null;
        }

        var filter = await BuildFilterAsync(ToView(search).Criteria, cancellationToken);
        return filter.IsSuccess ? await contacts.Filter(filter.Value!).Select(c => c.Id).ToListAsync(cancellationToken) : [];
    }

    private static SavedSearchView ToView(SavedSearch search) =>
        new(search.Id, search.Name, JsonSerializer.Deserialize<List<SearchCriterion>>(search.Criteria) ?? []);
}

internal sealed class ContactsDashboard(ContactsDbContext db, TimeProvider clock) : Arma.SharedKernel.Modules.IDashboardContributor
{
    public async Task<IReadOnlyList<Arma.SharedKernel.Modules.DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetLocalNow();
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
        var total = await db.Contacts.CountAsync(cancellationToken);
        var added = await db.Contacts.CountAsync(c => c.CreatedAt >= monthStart, cancellationToken);
        return [new("Kişi ve kurum", total.ToString("N0", Turkish), $"Bu ay {added.ToString("N0", Turkish)} yeni kayıt", "contacts", 10, ContactsPermissions.Read)];
    }

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
}
