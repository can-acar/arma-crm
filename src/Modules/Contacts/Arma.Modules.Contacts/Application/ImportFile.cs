using System.Text;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Modules;

namespace Arma.Modules.Contacts.Application;

/// <summary>Veri aktarımı ekranına yüklenen CSV dosyasını satırlara çevirir.</summary>
public static class ImportFile
{
    public const long MaxBytes = 50 * 1024 * 1024;

    public const int MaxRows = 200_000;

    public static Result<IReadOnlyList<ImportRow>> Read(byte[] content)
    {
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var rows = Csv.Read(reader).Take(MaxRows + 2).ToList();
        if (rows.Count < 2)
        {
            return new Error("import.empty", "Dosyada başlık satırından sonra kayıt yok.");
        }

        if (rows.Count > MaxRows + 1)
        {
            return new Error("import.too_many", $"Bir dosyada en fazla {MaxRows:N0} satır olabilir; dosyayı bölün.");
        }

        var header = rows[0].Select(h => h.Trim()).ToArray();
        return rows.Skip(1).Select((values, index) =>
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.Length && i < values.Length; i++)
            {
                map.TryAdd(header[i], values[i]);
                // Seçili alanlarla yapılan dışa aktarmada konum eklenir: "Email (Home)". Sade ad da tanınsın.
                var paren = header[i].IndexOf(" (", StringComparison.Ordinal);
                if (paren > 0 && header[i].EndsWith(')'))
                {
                    map.TryAdd(header[i][..paren], values[i]);
                }
            }

            return new ImportRow(index + 2, map);
        }).ToList();
    }

    /// <summary>Bulunmayan zorunlu sütunlar ("A|B" seçeneklerinden hiçbiri yoksa "A veya B").</summary>
    public static IReadOnlyList<string> MissingColumns(IDataImporter importer, IReadOnlyList<ImportRow> rows)
    {
        var present = rows.SelectMany(r => r.Values.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. importer.RequiredColumns
            .Where(required => !required.Split('|').Any(present.Contains))
            .Select(required => required.Replace("|", " veya "))];
    }
}
