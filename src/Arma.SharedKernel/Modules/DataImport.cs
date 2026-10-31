using System.Globalization;

namespace Arma.SharedKernel.Modules;

/// <summary>İçe aktarılan dosyanın bir satırı. Sütun adları büyük/küçük harf duyarsızdır.</summary>
public sealed record ImportRow(int Line, IReadOnlyDictionary<string, string> Values)
{
    /// <summary>Verilen sütunlardan ilk dolu olanın değeri; eski sistemlerde aynı alan farklı adlarla dışa aktarılabilir.</summary>
    public string? Get(params string[] columns)
    {
        foreach (var column in columns)
        {
            if (Values.TryGetValue(column, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}

public sealed record ImportIssue(int Line, string Name, string Reason);

/// <param name="Created">Yeni açılan kayıt.</param>
/// <param name="Matched">Arma'da zaten olan ve eski sistemdeki kimliği bağlanan kayıt.</param>
/// <param name="Skipped">Daha önce aktarılmış ya da kapsam dışı olduğu için atlanan satır.</param>
public sealed record ImportOutcome(int Created, int Matched, int Skipped, IReadOnlyList<ImportIssue> Issues);

/// <summary>
/// Başka bir sistemden (CiviCRM gibi) dışa aktarılmış bir dosyayı kaydın sahibi modüle aktarır. Aktarım ekranı
/// dosyayı okur, adımları <see cref="Order"/> sırasıyla gösterir; kayıtlar eski sistemdeki kimlikleriyle saklandığı için
/// aynı dosya yeniden yüklenirse mükerrer kayıt açılmaz.
/// </summary>
public interface IDataImporter
{
    /// <summary>Kaynak sistem anahtarı ("civicrm").</summary>
    string Source { get; }

    string Label { get; }

    /// <summary>Kaynak sistemde hangi dışa aktarmanın kullanılacağı.</summary>
    string Hint { get; }

    int Order { get; }

    string Permission { get; }

    /// <summary>Dosyada bulunması gereken sütunlar; "A|B" biçimi seçeneklerden birinin yeterli olduğunu belirtir.</summary>
    IReadOnlyList<string> RequiredColumns { get; }

    Task<ImportOutcome> ImportAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken);
}

/// <summary>Eski sistem kayıtlarının Arma'daki dış anahtarı: "civicrm:42".</summary>
public static class ExternalKeys
{
    public static string For(string source, string id) => $"{source}:{id.Trim()}";
}

/// <summary>Eski sistem dosyalarındaki tarih ve tutarları okur.</summary>
public static class ImportParsing
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"2024-03-05 14:22:00", "2024-03-05", "05.03.2024" ve "03/05/2024" (ABD) biçimlerini okur.</summary>
    public static DateOnly? Date(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        if (value.Length >= 10 && DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var iso))
        {
            return iso;
        }

        return DateOnly.TryParseExact(value.Split(' ')[0], ["dd.MM.yyyy", "d.M.yyyy", "MM/dd/yyyy", "M/d/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>"1250.00", "1,250.00" ve Türkçe "1.250,00" tutarlarını okur; para birimi simgeleri atılır.</summary>
    public static bool TryAmount(string? value, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = new string(value.Where(c => char.IsDigit(c) || c is '.' or ',' or '-').ToArray());
        var lastComma = text.LastIndexOf(',');
        var lastDot = text.LastIndexOf('.');
        var turkish = lastComma > lastDot && text.Length - lastComma - 1 is 1 or 2;
        return decimal.TryParse(text, NumberStyles.Number, turkish ? Turkish : CultureInfo.InvariantCulture, out amount);
    }
}
