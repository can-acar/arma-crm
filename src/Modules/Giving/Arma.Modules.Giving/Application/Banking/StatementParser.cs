using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Arma.Modules.Giving.Domain;
using Arma.SharedKernel.Domain;

namespace Arma.Modules.Giving.Application.Banking;

public sealed record ParsedStatement(string Format, IReadOnlyList<StatementRow> Rows);

/// <summary>
/// Banka ekstresini okur: Excel (.xlsx), CSV ve SWIFT MT940. Bankalar farklı sütun adları kullandığı için
/// Excel ve CSV'de başlık satırı "tarih", "tutar"/"alacak", "açıklama" gibi sözcüklerle bulunur.
/// </summary>
public static partial class StatementParser
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly string[] DateFormats =
        ["dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd.MM.yyyy HH:mm", "dd.MM.yyyy HH:mm:ss", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss"];

    public static Result<ParsedStatement> Parse(string fileName, byte[] content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        try
        {
            if (extension == ".xlsx" || (content.Length > 4 && content[0] == 'P' && content[1] == 'K'))
            {
                return FromTable("xlsx", ReadXlsx(content));
            }

            var text = Decode(content);
            if (extension is ".sta" or ".mt940" or ".940" || text.Contains(":61:", StringComparison.Ordinal))
            {
                return ReadMt940(text);
            }

            return FromTable("csv", ReadCsv(text));
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or System.Xml.XmlException)
        {
            return new Error("bank.unreadable", "Dosya okunamadı. Bankanın Excel (.xlsx), CSV veya MT940 ekstresini yükleyin.");
        }
    }

    /// <summary>Tutar: "1.250,50", "1250.50", "-1.250,50 TL", "1.250,50 B" (borç).</summary>
    public static bool TryParseAmount(string? text, out decimal amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().Replace("TL", "", StringComparison.OrdinalIgnoreCase).Replace("TRY", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Replace(" ", "");
        var negative = value.StartsWith('-') || value.EndsWith('-') || value.EndsWith('B') || value.StartsWith('(');
        value = value.Trim('-', '+', '(', ')', 'A', 'B');
        var comma = value.LastIndexOf(',');
        var dot = value.LastIndexOf('.');
        if (comma > dot)
        {
            value = value.Replace(".", "").Replace(',', '.');
        }
        else if (dot > comma && comma >= 0)
        {
            value = value.Replace(",", "");
        }
        else if (dot >= 0 && value.Length - dot - 1 == 3 && value.Count(c => c == '.') >= 1 && !value.Contains(','))
        {
            // "1.250" gibi yalnızca binlik ayraçlı tam sayı.
            value = value.Replace(".", "");
        }

        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            return false;
        }

        amount = negative ? -amount : amount;
        return true;
    }

    public static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (DateTime.TryParseExact(text, DateFormats, Turkish, DateTimeStyles.None, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }

        // Excel'de tarih hücresi sayı olarak gelir (1900 tabanlı gün sayısı).
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 20000 and < 80000)
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            return true;
        }

        return false;
    }

    public static string? FindIban(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var match = IbanPattern().Match(text.Replace(" ", ""));
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static Result<ParsedStatement> FromTable(string format, IReadOnlyList<string[]> rows)
    {
        for (var i = 0; i < Math.Min(rows.Count, 30); i++)
        {
            var header = rows[i].Select(Normalize).ToArray();
            var date = Find(header, "işlem tarihi") ?? Find(header, "tarih") ?? Find(header, "valör");
            var credit = Find(header, "alacak");
            var debit = Find(header, "borç");
            var amount = Find(header, "işlem tutarı") ?? Find(header, "tutar") ?? Find(header, "miktar");
            if (date is null || (credit is null && amount is null))
            {
                continue;
            }

            var description = Find(header, "açıklama") ?? Find(header, "aciklama");
            var counterparty = Find(header, "gönderen") ?? Find(header, "karşı") ?? Find(header, "unvan") ?? Find(header, "ad soyad");
            var reference = Find(header, "dekont") ?? Find(header, "referans") ?? Find(header, "fiş") ?? Find(header, "işlem no");
            var iban = Find(header, "iban");
            var result = new List<StatementRow>();
            foreach (var row in rows.Skip(i + 1))
            {
                if (!TryParseDate(Cell(row, date), out var bookedOn))
                {
                    continue;
                }

                decimal value;
                if (credit is not null && TryParseAmount(Cell(row, credit), out var c) && c != 0)
                {
                    value = Math.Abs(c);
                }
                else if (debit is not null && TryParseAmount(Cell(row, debit), out var d) && d != 0)
                {
                    value = -Math.Abs(d);
                }
                else if (amount is not null && TryParseAmount(Cell(row, amount), out var a))
                {
                    value = a;
                }
                else
                {
                    continue;
                }

                var text = Cell(row, description);
                result.Add(new(bookedOn, value, text, Cell(row, counterparty), FindIban(Cell(row, iban)) ?? FindIban(text), Cell(row, reference)));
            }

            return new ParsedStatement(format, result);
        }

        return new Error("bank.no_header", "Ekstrede başlık satırı bulunamadı. Dosyada \"Tarih\", \"Tutar\" (veya \"Alacak\") ve \"Açıklama\" sütunları olmalı.");
    }

    private static int? Find(string[] header, string word)
    {
        var index = Array.FindIndex(header, h => h.Contains(word, StringComparison.Ordinal));
        return index < 0 ? null : index;
    }

    private static string? Cell(string[] row, int? index) => index is { } i && i < row.Length && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;

    private static string Normalize(string text) => text.Trim().ToLower(Turkish);

    private static string Decode(byte[] content)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(content).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            // Türk bankalarının eski CSV dosyaları Windows-1254 kodlamasıyla gelir.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1254).GetString(content);
        }
    }

    private static List<string[]> ReadCsv(string text)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        var sample = string.Join('\n', lines.Take(20));
        var separator = new[] { ';', '\t', ',' }.MaxBy(s => sample.Count(c => c == s));
        return [.. lines.Select(l => SplitCsv(l, separator))];
    }

    private static string[] SplitCsv(string line, char separator)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == separator && !quoted)
            {
                cells.Add(cell.ToString());
                cell.Clear();
            }
            else
            {
                cell.Append(c);
            }
        }

        cells.Add(cell.ToString());
        return [.. cells];
    }

    /// <summary>İlk çalışma sayfasını okur. Ek kütüphane gerektirmemek için yalnızca hücre değerleri alınır.</summary>
    private static List<string[]> ReadXlsx(byte[] content)
    {
        using var zip = new ZipArchive(new MemoryStream(content), ZipArchiveMode.Read);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } strings)
        {
            using var stream = strings.Open();
            shared = [.. XDocument.Load(stream).Root!.Elements(ns + "si").Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value)))];
        }

        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml")
            ?? zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal) && e.FullName.EndsWith(".xml", StringComparison.Ordinal))
            ?? throw new InvalidDataException("Çalışma sayfası yok.");
        using var sheetStream = sheet.Open();
        var rows = new List<string[]>();
        foreach (var row in XDocument.Load(sheetStream).Descendants(ns + "row"))
        {
            var cells = new SortedDictionary<int, string>();
            var next = 0;
            foreach (var cell in row.Elements(ns + "c"))
            {
                var column = cell.Attribute("r") is { } r ? ColumnIndex(r.Value) : next;
                next = column + 1;
                var type = cell.Attribute("t")?.Value;
                var value = type == "inlineStr"
                    ? string.Concat(cell.Descendants(ns + "t").Select(t => t.Value))
                    : cell.Element(ns + "v")?.Value ?? "";
                if (type == "s" && int.TryParse(value, out var index) && index < shared.Count)
                {
                    value = shared[index];
                }

                cells[column] = value;
            }

            if (cells.Count > 0)
            {
                var values = new string[cells.Keys.Max() + 1];
                Array.Fill(values, "");
                foreach (var (column, value) in cells)
                {
                    values[column] = value;
                }

                rows.Add(values);
            }
        }

        return rows;
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
        {
            index = (index * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return index - 1;
    }

    /// <summary>MT940: ":61:" satırı tarih, alacak/borç ve tutarı; ardından gelen ":86:" açıklamayı taşır.</summary>
    private static Result<ParsedStatement> ReadMt940(string text)
    {
        var rows = new List<StatementRow>();
        var lines = text.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith(":61:", StringComparison.Ordinal))
            {
                continue;
            }

            var match = Mt940Line().Match(lines[i][4..]);
            if (!match.Success)
            {
                continue;
            }

            var date = DateOnly.ParseExact(match.Groups["date"].Value, "yyMMdd", CultureInfo.InvariantCulture);
            var amount = decimal.Parse(match.Groups["amount"].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            var mark = match.Groups["mark"].Value;
            var credit = mark is "C" or "RD";
            var rest = match.Groups["rest"].Value;
            var reference = rest.Contains("//", StringComparison.Ordinal) ? rest[(rest.IndexOf("//", StringComparison.Ordinal) + 2)..] : null;
            var description = new StringBuilder();
            var j = i + 1;
            for (; j < lines.Length && !lines[j].StartsWith(":61:", StringComparison.Ordinal) && !lines[j].StartsWith(":62", StringComparison.Ordinal); j++)
            {
                var line = lines[j].StartsWith(":86:", StringComparison.Ordinal) ? lines[j][4..] : lines[j];
                if (!lines[j].StartsWith(':') || lines[j].StartsWith(":86:", StringComparison.Ordinal))
                {
                    description.Append(line.Trim()).Append(' ');
                }
            }

            var details = description.ToString().Trim();
            rows.Add(new(date, credit ? amount : -amount, details, null, FindIban(details), reference?.Trim()));
            i = j - 1;
        }

        return rows.Count == 0
            ? new Error("bank.empty", "MT940 dosyasında hareket bulunamadı.")
            : new ParsedStatement("mt940", rows);
    }

    [GeneratedRegex(@"TR\d{24}", RegexOptions.IgnoreCase)]
    private static partial Regex IbanPattern();

    [GeneratedRegex(@"^(?<date>\d{6})(?<entry>\d{4})?(?<mark>RC|RD|C|D)[A-Z]?(?<amount>\d+,\d{0,2})(?<rest>.*)$")]
    private static partial Regex Mt940Line();
}
