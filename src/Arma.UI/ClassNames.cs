namespace Arma.UI;

/// <summary>
/// shadcn/ui'deki <c>cn()</c> yardımcısının karşılığı: boş olmayan sınıfları birleştirir.
/// Bileşenin temel sınıfları önce, çağıranın <c>Class</c> değeri en sonda gelir.
/// Not: tailwind-merge gibi çakışan yardımcı sınıfları ayıklamaz; çakışma gerekiyorsa
/// bileşene yeni bir varyant eklenir.
/// </summary>
public static class ClassNames
{
    public static string Cn(params string?[] classes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var tokens = new List<string>();

        foreach (var value in classes)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(token))
                {
                    tokens.Add(token);
                }
            }
        }

        return string.Join(' ', tokens);
    }
}
