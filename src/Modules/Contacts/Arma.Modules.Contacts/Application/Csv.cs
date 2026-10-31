using System.Text;

namespace Arma.Modules.Contacts.Application;

/// <summary>
/// Küçük bir CSV okuyucu/yazıcı. Türkçe Excel varsayılanı olan noktalı virgülü ve virgülü tanır;
/// tırnak içindeki ayırıcıları ve satır sonlarını destekler.
/// </summary>
public static class Csv
{
    public static char DetectDelimiter(string headerLine) =>
        headerLine.Count(c => c == ';') >= headerLine.Count(c => c == ',') ? ';' : ',';

    public static IEnumerable<string[]> Read(TextReader reader)
    {
        var first = true;
        var delimiter = ';';
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var buffer = reader.ReadToEnd();
        if (buffer.Length > 0 && buffer[0] == '﻿')
        {
            buffer = buffer[1..];
        }

        for (var i = 0; i < buffer.Length; i++)
        {
            if (first)
            {
                var end = buffer.IndexOf('\n');
                delimiter = DetectDelimiter(end < 0 ? buffer : buffer[..end]);
                first = false;
            }

            var c = buffer[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < buffer.Length && buffer[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < buffer.Length && buffer[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                if (row.Any(f => f.Length > 0))
                {
                    yield return [.. row];
                }

                row.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            if (row.Any(f => f.Length > 0))
            {
                yield return [.. row];
            }
        }
    }

    public static void WriteRow(StringBuilder output, IEnumerable<string?> values, char delimiter = ';')
    {
        output.AppendJoin(delimiter, values.Select(v => Escape(v, delimiter)));
        output.Append("\r\n");
    }

    private static string Escape(string? value, char delimiter)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Excel'de formül enjeksiyonunu önle.
        if (value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([delimiter, '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
