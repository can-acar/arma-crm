using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Application;

/// <summary>
/// CiviCRM "Kişileri dışa aktar" (Export Contacts) dosyasını aktarır. Kayıtlar "civicrm:{Contact ID}" anahtarıyla saklanır;
/// aynı e-postayla Arma'da zaten olan kişiye yeni kayıt açılmaz, anahtar o kayda bağlanır.
/// </summary>
public sealed class CivicrmContactImporter(ContactsDbContext db, IAuditLog audit) : IDataImporter
{
    public const string SourceKey = "civicrm";

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public string Source => SourceKey;

    public string Label => "Kişiler";

    public string Hint => "Kişileri Ara → tümünü seç → Kişileri dışa aktar → \"Birincil alanlar\". Diğer adımlar kişileri Contact ID ile bulduğu için önce bu dosya aktarılır.";

    public int Order => 10;

    public string Permission => ContactsPermissions.Import;

    public IReadOnlyList<string> RequiredColumns => ["Contact ID", "First Name|Last Name|Organization Name|Household Name|Display Name"];

    public async Task<ImportOutcome> ImportAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        var issues = new List<ImportIssue>();
        var keys = rows.Select(r => r.Get("Contact ID", "Internal Contact ID")).OfType<string>().Select(id => ExternalKeys.For(SourceKey, id)).ToHashSet();
        var known = (await db.Contacts.IgnoreQueryFilters().Where(c => c.TenantId == db.TenantId && c.ExternalKey != null && keys.Contains(c.ExternalKey))
            .Select(c => c.ExternalKey!).ToListAsync(cancellationToken)).ToHashSet();
        var byEmail = (await db.Contacts.Where(c => c.Email != null && c.ExternalKey == null).Select(c => new { c.Id, c.Email }).ToListAsync(cancellationToken))
            .GroupBy(c => c.Email!).ToDictionary(g => g.Key, g => g.First().Id);
        var nationalIds = (await db.Contacts.Where(c => c.NationalId != null).Select(c => c.NationalId!).ToListAsync(cancellationToken)).ToHashSet();
        var tags = await db.Tags.ToDictionaryAsync(t => t.Name, StringComparer.Create(Turkish, ignoreCase: true), cancellationToken);
        var groups = await db.Groups.ToDictionaryAsync(g => g.Name, StringComparer.Create(Turkish, ignoreCase: true), cancellationToken);
        int created = 0, matched = 0, skipped = 0;

        foreach (var row in rows)
        {
            var id = row.Get("Contact ID", "Internal Contact ID");
            var name = row.Get("Display Name", "Sort Name") ?? $"{row.Get("First Name")} {row.Get("Last Name")}".Trim();
            if (id is null)
            {
                issues.Add(new ImportIssue(row.Line, name, "Contact ID boş."));
                continue;
            }

            var key = ExternalKeys.For(SourceKey, id);
            if (known.Contains(key) || IsYes(row.Get("Contact is in Trash", "Is Deleted")))
            {
                skipped++;
                continue;
            }

            known.Add(key);
            var email = row.Get("Email")?.ToLowerInvariant();
            if (email is not null && byEmail.Remove(email, out var existingId))
            {
                var existing = await db.Contacts.FirstAsync(c => c.Id == existingId, cancellationToken);
                existing.LinkExternal(key);
                matched++;
                continue;
            }

            var result = Create(row);
            if (!result.IsSuccess)
            {
                issues.Add(new ImportIssue(row.Line, name, result.Error!.Message));
                continue;
            }

            var contact = result.Value!;
            contact.LinkExternal(key);
            Address(contact, row);
            foreach (var tagName in List(row.Get("Tag(s)", "Tags")))
            {
                if (!tags.TryGetValue(tagName, out var tag))
                {
                    if (Tag.Create(tagName, null) is not { IsSuccess: true } newTag)
                    {
                        continue;
                    }

                    tag = tags[tagName] = newTag.Value!;
                    db.Tags.Add(tag);
                }

                contact.Tags.Add(tag);
            }

            foreach (var groupName in List(row.Get("Group(s)", "Groups")))
            {
                if (!groups.TryGetValue(groupName, out var group))
                {
                    if (Group.Create(groupName, "CiviCRM'den aktarıldı") is not { IsSuccess: true } newGroup)
                    {
                        continue;
                    }

                    group = groups[groupName] = newGroup.Value!;
                    db.Groups.Add(group);
                }

                contact.Groups.Add(group);
            }

            if (contact.Email is not null)
            {
                // Dosyada aynı e-posta iki kez geçerse ikinci kayıt da açılır; mükerrerler ekranından birleştirilebilir.
                byEmail.Remove(contact.Email);
            }

            db.Contacts.Add(contact);
            created++;
            if ((created + matched) % 500 == 0)
            {
                await FlushAsync();
            }
        }

        await FlushAsync();
        await audit.WriteAsync(AuditAction.Imported, nameof(Contact), null,
            $"CiviCRM: {created} kişi aktarıldı, {matched} kişi eşleşti, {skipped} satır atlandı, {issues.Count} satır hatalı", cancellationToken);
        return new ImportOutcome(created, matched, skipped, issues);

        // Büyük dosyalarda izlenen kayıt sayısı büyüdükçe kayıt yavaşlar; her partiden sonra izleme sıfırlanır.
        async Task FlushAsync()
        {
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            db.Tags.AttachRange(tags.Values);
            db.Groups.AttachRange(groups.Values);
        }

        Result<Contact> Create(ImportRow row)
        {
            var email = row.Get("Email");
            var phone = row.Get("Phone");
            switch (row.Get("Contact Type")?.ToLowerInvariant())
            {
                case "organization" or "kurum":
                    var organization = Contact.CreateOrganization(row.Get("Organization Name", "Display Name") ?? "", email, phone, null);
                    if (organization.IsSuccess && row.Get("Website") is { } web)
                    {
                        var org = organization.Value!;
                        org.UpdateOrganization(org.OrganizationName!, org.Email, org.Phone, null, web);
                    }

                    return organization;
                case "household" or "hane":
                    return Contact.CreateHousehold(row.Get("Household Name", "Display Name") ?? "", email, phone);
                default:
                    var (first, last) = PersonName(row);
                    var person = Contact.CreatePerson(first ?? "", last ?? "", email, phone, NationalId(row));
                    if (person.IsSuccess && ImportParsing.Date(row.Get("Birth Date")) is { } birth)
                    {
                        var p = person.Value!;
                        p.UpdatePerson(p.FirstName!, p.LastName!, p.Email, p.Phone, p.NationalId, birth);
                    }

                    return person;
            }
        }

        // CiviCRM'de TCKN için standart alan yok; "External Identifier" alanına yazılmışsa ve geçerliyse alınır.
        string? NationalId(ImportRow row)
        {
            var value = row.Get("External Identifier");
            return value is not null && TurkishIdentityNumber.IsValidTckn(value) && nationalIds.Add(value) ? value : null;
        }
    }

    /// <summary>Ad veya soyad boşsa görünen ad son boşluktan bölünür ("Ayşe Nur Demir" → "Ayşe Nur" + "Demir").</summary>
    internal static (string? First, string? Last) PersonName(ImportRow row)
    {
        var first = row.Get("First Name");
        var middle = row.Get("Middle Name");
        var last = row.Get("Last Name");
        if (first is not null && last is not null)
        {
            return (middle is null ? first : $"{first} {middle}", last);
        }

        var display = row.Get("Display Name", "Addressee") ?? first ?? last;
        var space = display?.LastIndexOf(' ') ?? -1;
        return space > 0 ? (display![..space].Trim(), display[(space + 1)..].Trim()) : (first, last);
    }

    /// <summary>CiviCRM'de il "State", ilçe/şehir "City" sütunundadır; il yoksa şehir il sayılır.</summary>
    private static void Address(Contact contact, ImportRow row)
    {
        var state = row.Get("State", "State/Province");
        var city = row.Get("City");
        var line = string.Join(" ", new[] { row.Get("Street Address"), row.Get("Supplemental Address 1"), row.Get("Supplemental Address 2") }.OfType<string>());
        var country = row.Get("Country");
        var address = new Address
        {
            Line = line.Length == 0 ? null : line,
            City = state ?? city,
            District = state is null ? null : city,
            PostalCode = row.Get("Postal Code"),
            Country = country is null or "Turkey" or "Türkiye" or "TR" ? "TR" : country.Length == 2 ? country.ToUpperInvariant() : country,
            Kind = row.Get("Location Type")?.ToLowerInvariant() switch
            {
                "work" or "iş" => AddressKind.Work,
                "billing" or "fatura" => AddressKind.Billing,
                "home" or "ev" or null => AddressKind.Home,
                _ => AddressKind.Other,
            },
            IsPrimary = true,
        };
        if (!address.IsEmpty)
        {
            contact.SetAddresses([address]);
        }
    }

    private static bool IsYes(string? value) => value is "1" or "Yes" or "yes" or "Evet" or "true";

    private static IEnumerable<string> List(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Create(Turkish, ignoreCase: true));
}
