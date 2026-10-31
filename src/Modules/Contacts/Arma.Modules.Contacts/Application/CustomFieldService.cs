using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Application;

public sealed record CustomFieldView(
    Guid Id, string Label, CustomFieldType Type, IReadOnlyList<string> Options, ContactType? AppliesTo, string? HelpText, int Order, bool IsActive, int UsedCount);

public sealed record CustomFieldValue(Guid FieldId, string Label, CustomFieldType Type, IReadOnlyList<string> Options, string? HelpText, string? Value, string? Display);

/// <summary>Kişi kartı ek alanlarının tanımı, değerleri ve gelişmiş aramadaki ölçütleri.</summary>
public sealed class CustomFieldService(ContactsDbContext db) : IContactCriteriaProvider
{
    private const string CriteriaGroup = "Ek alanlar";

    public static readonly Error NotFound = new("custom_fields.not_found", "Alan bulunamadı.");

    public async Task<IReadOnlyList<CustomFieldView>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var fields = await Fields(activeOnly).ToListAsync(cancellationToken);
        var counts = new Dictionary<Guid, int>();
        foreach (var field in fields)
        {
            counts[field.Id] = await UsedCountAsync(field.Id, cancellationToken);
        }

        return [.. fields.Select(f => new CustomFieldView(f.Id, f.Label, f.Type, f.Options, f.AppliesTo, f.HelpText, f.Order, f.IsActive, counts[f.Id]))];
    }

    public async Task<Result<Guid>> SaveAsync(Guid? id, CustomFieldInput input, CancellationToken cancellationToken = default)
    {
        CustomField field;
        if (id is null)
        {
            var created = CustomField.Create(input);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            field = created.Value!;
            db.CustomFields.Add(field);
        }
        else
        {
            var existing = await db.CustomFields.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (existing is null)
            {
                return NotFound;
            }

            // Kullanılan alanın türü değişirse kayıtlı değerler yeni türe uymaz.
            if (existing.Type != input.Type && await UsedCountAsync(existing.Id, cancellationToken) > 0)
            {
                return new Error("custom_fields.type_locked", "Değer girilmiş alanın türü değiştirilemez; yeni bir alan açın.");
            }

            var updated = existing.Update(input);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }

            field = existing;
        }

        if (await db.CustomFields.AnyAsync(f => f.Id != field.Id && f.Label == field.Label, cancellationToken))
        {
            return new Error("custom_fields.duplicate", "Bu adda bir alan zaten var.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return field.Id;
    }

    /// <summary>Alanı siler; kişilerdeki değerleri de temizlenir.</summary>
    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var field = await db.CustomFields.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (field is null)
        {
            return NotFound;
        }

        db.CustomFields.Remove(field);
        await db.SaveChangesAsync(cancellationToken);
        var key = id.ToString();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE contacts.contacts SET custom_fields_json = custom_fields_json - {key} WHERE custom_fields_json ? {key}", cancellationToken);
        return id;
    }

    /// <summary>Kişinin türüne uyan etkin alanlar ve değerleri.</summary>
    public async Task<IReadOnlyList<CustomFieldValue>> ValuesAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var contact = await db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null)
        {
            return [];
        }

        var values = contact.CustomFields;
        var fields = await Fields(activeOnly: true).ToListAsync(cancellationToken);
        return [.. fields.Where(f => f.AppliesToType(contact.Type)).Select(f =>
        {
            var value = values.GetValueOrDefault(f.Id.ToString());
            return new CustomFieldValue(f.Id, f.Label, f.Type, f.Options, f.HelpText, value, value is null ? null : f.Display(value));
        })];
    }

    /// <summary>Verilen alanların değerlerini kaydeder. Boş değer alanı temizler; verilmeyen alanlar değişmez.</summary>
    public async Task<Result<Guid>> SaveValuesAsync(Guid contactId, IReadOnlyDictionary<Guid, string?> input, CancellationToken cancellationToken = default)
    {
        var contact = await db.Contacts.FirstOrDefaultAsync(c => c.Id == contactId, cancellationToken);
        if (contact is null)
        {
            return ContactErrors.NotFound;
        }

        var fields = await db.CustomFields.Where(f => input.Keys.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);
        var values = new Dictionary<string, string>(contact.CustomFields);
        foreach (var (fieldId, raw) in input)
        {
            if (!fields.TryGetValue(fieldId, out var field) || !field.AppliesToType(contact.Type))
            {
                return NotFound;
            }

            var normalized = field.Normalize(raw);
            if (!normalized.IsSuccess)
            {
                return normalized.Error!;
            }

            if (normalized.Value is { } value)
            {
                values[fieldId.ToString()] = value;
            }
            else
            {
                values.Remove(fieldId.ToString());
            }
        }

        contact.SetCustomFields(values);
        await db.SaveChangesAsync(cancellationToken);
        return contactId;
    }

    public async Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken)
    {
        var fields = await Fields(activeOnly: true).ToListAsync(cancellationToken);
        return [.. fields.Select(f => f.Type switch
        {
            CustomFieldType.Number => new CriterionDefinition($"custom_min:{f.Id}", CriteriaGroup, $"{f.Label} en az", CriterionKind.Number),
            CustomFieldType.Date => new CriterionDefinition($"custom_since:{f.Id}", CriteriaGroup, $"{f.Label} bu tarihten sonra", CriterionKind.Date),
            CustomFieldType.YesNo => new CriterionDefinition($"custom_yes:{f.Id}", CriteriaGroup, $"{f.Label}: evet", CriterionKind.Flag),
            CustomFieldType.Choice => new CriterionDefinition($"custom:{f.Id}", CriteriaGroup, f.Label, CriterionKind.Choice, [.. f.Options.Select(o => new CriterionOption(o, o))]),
            _ => new CriterionDefinition($"custom:{f.Id}", CriteriaGroup, $"{f.Label} içerir", CriterionKind.Text),
        })];
    }

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken)
    {
        var colon = key.IndexOf(':');
        if (colon < 0 || !Guid.TryParse(key[(colon + 1)..], out var fieldId))
        {
            return null;
        }

        var field = await db.CustomFields.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fieldId, cancellationToken);
        if (field is null)
        {
            return null;
        }

        var id = fieldId.ToString();
        IQueryable<Guid> query;
        switch (key[..colon])
        {
            case "custom_yes":
                query = Ids($"SELECT id AS \"Value\" FROM contacts.contacts WHERE NOT is_deleted AND custom_fields_json ->> {id} = 'true'");
                break;
            case "custom_min" when value is not null && CustomField.TryParseNumber(value, out var min):
                query = Ids($"SELECT id AS \"Value\" FROM contacts.contacts WHERE NOT is_deleted AND (custom_fields_json ->> {id})::numeric >= {min}");
                break;
            case "custom_since" when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var since):
                var text = since.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                query = Ids($"SELECT id AS \"Value\" FROM contacts.contacts WHERE NOT is_deleted AND custom_fields_json ->> {id} >= {text}");
                break;
            case "custom" when !string.IsNullOrWhiteSpace(value) && field.Type == CustomFieldType.Choice:
                query = Ids($"SELECT id AS \"Value\" FROM contacts.contacts WHERE NOT is_deleted AND custom_fields_json ->> {id} = {value.Trim()}");
                break;
            case "custom" when !string.IsNullOrWhiteSpace(value):
                var pattern = "%" + value.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                query = Ids($"SELECT id AS \"Value\" FROM contacts.contacts WHERE NOT is_deleted AND custom_fields_json ->> {id} ILIKE {pattern}");
                break;
            default:
                return null;
        }

        return new CriterionMatch(await query.ToListAsync(cancellationToken));
    }

    internal Task<List<CustomField>> ActiveFieldsAsync(CancellationToken cancellationToken) => Fields(activeOnly: true).ToListAsync(cancellationToken);

    private IQueryable<Guid> Ids(FormattableString sql) => db.Database.SqlQuery<Guid>(sql);

    private IQueryable<CustomField> Fields(bool activeOnly) =>
        db.CustomFields.AsNoTracking().Where(f => !activeOnly || f.IsActive).OrderBy(f => f.Order).ThenBy(f => f.Label);

    private Task<int> UsedCountAsync(Guid fieldId, CancellationToken cancellationToken)
    {
        var key = fieldId.ToString();
        return db.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM contacts.contacts WHERE NOT is_deleted AND custom_fields_json ? {key}").SingleAsync(cancellationToken);
    }
}
