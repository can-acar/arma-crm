using System.Globalization;
using System.Text;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Application;

public sealed record ImportRowError(int Row, string Name, string Reason);

public sealed record ImportResult(int Created, int Total, IReadOnlyList<ImportRowError> Errors);

/// <summary>Kişileri CSV olarak dışa ve içe aktarır. Her iki işlem de denetim izine yazılır.</summary>
public sealed class ContactTransferService(ContactsDbContext db, ContactService contacts, IAuditLog audit)
{
    public const int MaxImportRows = 50_000;

    private static readonly string[] Header =
        ["Tür", "Ad", "Soyad", "Kurum/Hane", "E-posta", "Telefon", "Kimlik no", "Doğum tarihi", "Web", "İl", "İlçe", "Adres", "Posta kodu", "Etiketler"];

    public async Task<byte[]> ExportAsync(ContactFilter filter, bool includeNationalId, CancellationToken cancellationToken = default)
    {
        var rows = await contacts.Filter(filter).Include(c => c.Tags).OrderBy(c => c.DisplayName).AsSplitQuery().ToListAsync(cancellationToken);

        var output = new StringBuilder();
        Csv.WriteRow(output, Header);
        foreach (var c in rows)
        {
            var address = c.PrimaryAddress;
            Csv.WriteRow(output,
            [
                TypeName(c.Type), c.FirstName, c.LastName, c.Type == ContactType.Person ? null : c.OrganizationName, c.Email, c.Phone,
                includeNationalId ? c.NationalId : c.MaskedNationalId, c.BirthDate?.ToString("dd.MM.yyyy"), c.Website,
                address?.City, address?.District, address?.Line, address?.PostalCode, string.Join(", ", c.Tags.Select(t => t.Name)),
            ]);
        }

        await audit.WriteAsync(AuditAction.Exported, nameof(Contact), null,
            $"{rows.Count} kayıt dışa aktarıldı{(includeNationalId ? " (kimlik numaraları açık)" : "")}", cancellationToken);

        // Excel'in Türkçe karakterleri doğru açması için UTF-8 BOM.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(output.ToString())];
    }

    public async Task<Result<ImportResult>> ImportAsync(Stream csv, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rows = Csv.Read(reader).Take(MaxImportRows + 1).ToList();
        if (rows.Count < 2)
        {
            return new Error("import.empty", "Dosyada başlık satırından sonra kayıt yok.");
        }

        var columns = MapColumns(rows[0]);
        if (!columns.ContainsKey("ad") && !columns.ContainsKey("kurum/hane"))
        {
            return new Error("import.header", "Başlık satırında en az 'Ad' veya 'Kurum/Hane' sütunu olmalı. Örnek dosya için önce dışa aktarın.");
        }

        var errors = new List<ImportRowError>();
        var knownNationalIds = (await db.Contacts.Where(c => c.NationalId != null).Select(c => c.NationalId!).ToListAsync(cancellationToken)).ToHashSet();
        var knownEmails = (await db.Contacts.Where(c => c.Email != null).Select(c => c.Email!).ToListAsync(cancellationToken)).ToHashSet();
        var tags = await db.Tags.ToDictionaryAsync(t => t.Name, StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), ignoreCase: true), cancellationToken);
        var created = 0;

        foreach (var (row, index) in rows.Skip(1).Select((r, i) => (r, i + 2)))
        {
            string? Get(string name) => columns.TryGetValue(name, out var i) && i < row.Length && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;

            var orgName = Get("kurum/hane");
            var type = ParseType(Get("tür"), Get("ad"), orgName);
            var nationalId = Get("kimlik no");
            if (nationalId?.Contains('*') == true)
            {
                nationalId = null;
            }

            var result = type switch
            {
                ContactType.Organization => Contact.CreateOrganization(orgName ?? "", Get("e-posta"), Get("telefon"), nationalId),
                ContactType.Household => Contact.CreateHousehold(orgName ?? "", Get("e-posta"), Get("telefon")),
                _ => Contact.CreatePerson(Get("ad") ?? "", Get("soyad") ?? "", Get("e-posta"), Get("telefon"), nationalId),
            };

            var displayName = orgName ?? $"{Get("ad")} {Get("soyad")}".Trim();
            if (!result.IsSuccess)
            {
                errors.Add(new ImportRowError(index, displayName, result.Error!.Message));
                continue;
            }

            var contact = result.Value!;
            if (contact.NationalId is not null && !knownNationalIds.Add(contact.NationalId))
            {
                errors.Add(new ImportRowError(index, displayName, "Bu kimlik numarası zaten kayıtlı."));
                continue;
            }

            if (contact.Email is not null && !knownEmails.Add(contact.Email))
            {
                errors.Add(new ImportRowError(index, displayName, "Bu e-posta zaten kayıtlı; mükerrer olabilir."));
                continue;
            }

            if (type == ContactType.Person && DateOnly.TryParseExact(Get("doğum tarihi"), ["dd.MM.yyyy", "yyyy-MM-dd", "d.M.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth))
            {
                contact.UpdatePerson(contact.FirstName!, contact.LastName!, contact.Email, contact.Phone, contact.NationalId, birth);
            }
            else if (type != ContactType.Person && Get("web") is { } web)
            {
                contact.UpdateOrganization(contact.OrganizationName!, contact.Email, contact.Phone, contact.NationalId, web);
            }

            var address = new Address { City = Get("il"), District = Get("ilçe"), Line = Get("adres"), PostalCode = Get("posta kodu"), IsPrimary = true };
            if (!address.IsEmpty)
            {
                contact.SetAddresses([address]);
            }

            foreach (var tagName in (Get("etiketler") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!tags.TryGetValue(tagName, out var tag))
                {
                    var newTag = Tag.Create(tagName, null);
                    if (!newTag.IsSuccess)
                    {
                        continue;
                    }

                    tag = newTag.Value!;
                    tags[tagName] = tag;
                    db.Tags.Add(tag);
                }

                contact.Tags.Add(tag);
            }

            db.Contacts.Add(contact);
            created++;

            if (created % 500 == 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(AuditAction.Imported, nameof(Contact), null, $"{created} kayıt içe aktarıldı, {errors.Count} satır atlandı", cancellationToken);
        return new ImportResult(created, rows.Count - 1, errors);
    }

    /// <summary>Örnek dosya: yalnızca başlık ve bir örnek satır.</summary>
    public static byte[] Template()
    {
        var output = new StringBuilder();
        Csv.WriteRow(output, Header);
        Csv.WriteRow(output, ["Kişi", "Ayşe", "Yılmaz", "", "ayse@example.org", "0532 000 00 00", "", "15.04.1985", "", "İzmir", "Konak", "Alsancak Mah. 1453 Sok. No:5", "35220", "Gönüllü, Bülten"]);
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(output.ToString())];
    }

    private static Dictionary<string, int> MapColumns(string[] header)
    {
        var map = new Dictionary<string, int>();
        for (var i = 0; i < header.Length; i++)
        {
            var key = header[i].Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
            key = key switch
            {
                "email" or "eposta" or "e-mail" => "e-posta",
                "tckn" or "vkn" or "tc kimlik no" or "vergi no" => "kimlik no",
                "kurum" or "hane" or "kurum adı" => "kurum/hane",
                "şehir" => "il",
                "telefon no" or "gsm" or "cep" => "telefon",
                _ => key,
            };
            map.TryAdd(key, i);
        }

        return map;
    }

    private static ContactType ParseType(string? value, string? firstName, string? organization) =>
        value?.ToLower(CultureInfo.GetCultureInfo("tr-TR")) switch
        {
            "kurum" or "organization" => ContactType.Organization,
            "hane" or "household" => ContactType.Household,
            "kişi" or "person" => ContactType.Person,
            _ => firstName is null && organization is not null ? ContactType.Organization : ContactType.Person,
        };

    private static string TypeName(ContactType type) => type switch
    {
        ContactType.Organization => "Kurum",
        ContactType.Household => "Hane",
        _ => "Kişi",
    };
}
