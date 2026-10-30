using System.Text.RegularExpressions;

namespace Arma.UI;

/// <summary>
/// shadcn/ui'deki <c>cn()</c> yardımcısının karşılığı: boş olmayan sınıfları birleştirir.
/// Bileşenin temel sınıfları önce, çağıranın <c>Class</c> değeri en sonda gelir. Aynı gruptaki çakışan
/// yardımcı sınıflarda (genişlik, yükseklik, dolgu, metin boyutu gibi) tailwind-merge gibi sonuncusu kalır;
/// <c>w-full</c> ile <c>w-36</c> birlikte verilirse yalnızca <c>w-36</c> uygulanır.
/// </summary>
public static partial class ClassNames
{
    private static readonly HashSet<string> TextAlignments = ["left", "center", "right", "justify", "start", "end"];

    private static readonly HashSet<string> TextSizes = ["xs", "sm", "base", "lg", "xl", "2xl", "3xl", "4xl", "5xl", "6xl", "7xl", "8xl", "9xl"];

    public static string Cn(params string?[] classes)
    {
        var tokens = new List<string>();
        var byGroup = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var value in classes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tokens.Contains(token))
                {
                    continue;
                }

                var group = ConflictGroup(token);
                if (group is not null && byGroup.TryGetValue(group, out var index))
                {
                    tokens[index] = token;
                    continue;
                }

                if (group is not null)
                {
                    byGroup[group] = tokens.Count;
                }

                tokens.Add(token);
            }
        }

        return string.Join(' ', tokens);
    }

    /// <summary>
    /// Sınıfın çakışma grubu: varyant önekleri (<c>md:</c>, <c>hover:</c>) korunur, değer atılır.
    /// Tanınmayan sınıflar için null döner ve hiçbir şeyle çakışmaz.
    /// </summary>
    internal static string? ConflictGroup(string token)
    {
        var colon = token.LastIndexOf(':');
        var variant = colon >= 0 && !token.Contains('[') ? token[..(colon + 1)] : "";
        var utility = token[variant.Length..];

        var match = UtilityPattern().Match(utility);
        if (!match.Success)
        {
            return null;
        }

        var prefix = match.Groups["prefix"].Value;
        var rest = match.Groups["value"].Value;
        switch (prefix)
        {
            case "text" when TextAlignments.Contains(rest):
                prefix = "text-align";
                break;
            case "text" when TextSizes.Contains(rest) || (rest.StartsWith('[') && rest.Contains("px")):
                prefix = "text-size";
                break;
            case "text" when rest is "ellipsis" or "clip" or "wrap" or "nowrap" or "balance" or "pretty":
                return null;
            case "text":
                prefix = "text-color";
                break;
            case "font":
                prefix = rest is "sans" or "serif" or "mono" ? "font-family" : "font-weight";
                break;
            // Arka plan görseli, konumu ve tekrar ayarları renkle çakışmaz.
            case "bg" when rest.StartsWith('[') || rest.StartsWith("no-") || rest.StartsWith("repeat") || rest is "cover" or "contain" or "center" or "fixed" or "clip-padding":
                return null;
            // rounded-t-md gibi kenar bazlı köşeleri ayrı tut.
            case "rounded" when rest.Contains('-'):
                return null;
        }

        return variant + prefix;
    }

    [GeneratedRegex(@"^(?<prefix>min-w|max-w|min-h|max-h|w|h|size|px|py|pt|pr|pb|pl|p|mx|my|mt|mr|mb|ml|m|gap-x|gap-y|gap|text|bg|rounded|shadow|font|leading|tracking|grid-cols|col-span|z|opacity)-(?<value>.+)$")]
    private static partial Regex UtilityPattern();
}
