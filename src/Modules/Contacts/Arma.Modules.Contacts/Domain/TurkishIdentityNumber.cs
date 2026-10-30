namespace Arma.Modules.Contacts.Domain;

/// <summary>T.C. kimlik numarası (TCKN) ve vergi kimlik numarası (VKN) kontrolleri.</summary>
public static class TurkishIdentityNumber
{
    /// <summary>
    /// 11 haneli TCKN'yi resmi algoritmayla doğrular: ilk hane 0 olamaz,
    /// 10. hane tek ve çift hanelerden, 11. hane ilk 10 hanenin toplamından türetilir.
    /// </summary>
    public static bool IsValidTckn(string? value)
    {
        if (value is not { Length: 11 } || !value.All(char.IsAsciiDigit) || value[0] == '0')
        {
            return false;
        }

        var d = value.Select(c => c - '0').ToArray();
        var odd = d[0] + d[2] + d[4] + d[6] + d[8];
        var even = d[1] + d[3] + d[5] + d[7];
        var tenth = ((odd * 7) - even) % 10;
        if (tenth < 0)
        {
            tenth += 10;
        }

        return d[9] == tenth && d.Take(10).Sum() % 10 == d[10];
    }

    /// <summary>VKN için yalnızca biçim kontrolü (10 hane). Kontrol hanesi doğrulaması sonraki adımda eklenecek.</summary>
    public static bool IsValidVknFormat(string? value) => value is { Length: 10 } && value.All(char.IsAsciiDigit);

    /// <summary>Ekranda gösterim için ilk 3 ve son 2 hane dışını gizler: 100******46.</summary>
    public static string Mask(string value) =>
        value.Length <= 5 ? new string('*', value.Length) : value[..3] + new string('*', value.Length - 5) + value[^2..];
}
