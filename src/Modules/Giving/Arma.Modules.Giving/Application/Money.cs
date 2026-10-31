using System.Globalization;

namespace Arma.Modules.Giving.Application;

public static class Money
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>"1.250,00 TL" biçimi.</summary>
    public static string Format(decimal amount, string currency) =>
        string.Format(Turkish, "{0:N2} {1}", amount, currency == "TRY" ? "TL" : currency);

    /// <summary>Makbuzdaki "yalnız" satırı: 1250,50 → "bin iki yüz elli Türk lirası elli kuruş".</summary>
    public static string InWords(decimal amount, string currency)
    {
        var whole = (long)decimal.Truncate(amount);
        var cents = (int)decimal.Round((amount - whole) * 100);
        var (major, minor) = currency switch
        {
            "TRY" => ("Türk lirası", "kuruş"),
            "USD" => ("ABD doları", "sent"),
            "EUR" => ("avro", "sent"),
            _ => (currency, "/100"),
        };

        var text = $"{Words(whole)} {major}";
        return cents > 0 ? $"{text} {Words(cents)} {minor}" : text;
    }

    private static readonly string[] Ones = ["", "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz"];
    private static readonly string[] Tens = ["", "on", "yirmi", "otuz", "kırk", "elli", "altmış", "yetmiş", "seksen", "doksan"];
    private static readonly string[] Scales = ["", "bin", "milyon", "milyar", "trilyon"];

    internal static string Words(long number)
    {
        if (number == 0)
        {
            return "sıfır";
        }

        var parts = new List<string>();
        for (var scale = 0; number > 0; scale++, number /= 1000)
        {
            var group = (int)(number % 1000);
            if (group == 0)
            {
                continue;
            }

            // Türkçede "bir bin" denmez, yalnızca "bin".
            var groupText = scale == 1 && group == 1 ? "" : Hundreds(group);
            parts.Insert(0, string.Join(" ", new[] { groupText, Scales[scale] }.Where(p => p.Length > 0)));
        }

        return string.Join(" ", parts);
    }

    private static string Hundreds(int number)
    {
        var hundreds = number / 100;
        var rest = number % 100;
        var words = new List<string>();
        if (hundreds > 0)
        {
            words.Add(hundreds == 1 ? "yüz" : $"{Ones[hundreds]} yüz");
        }

        if (rest / 10 > 0)
        {
            words.Add(Tens[rest / 10]);
        }

        if (rest % 10 > 0)
        {
            words.Add(Ones[rest % 10]);
        }

        return string.Join(" ", words);
    }
}
