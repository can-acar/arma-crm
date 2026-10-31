using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Domain;

namespace Arma.Modules.Contacts.Domain;

public enum CustomFieldType
{
    Text = 1,
    Number = 2,
    Date = 3,
    YesNo = 4,
    Choice = 5,
}

public sealed record CustomFieldInput(string Label, CustomFieldType Type, IReadOnlyList<string> Options, ContactType? AppliesTo, string? HelpText, int Order, bool IsActive);

/// <summary>
/// Kurumun kişi kartına eklediği alan (meslek, kan grubu, gönüllülük alanı gibi). Değerler kişide
/// alan kimliğiyle saklanır; alanın adı değişse de değer kaybolmaz.
/// </summary>
public sealed class CustomField : Entity
{
    public const int MaxTextLength = 500;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private CustomField()
    {
    }

    public string Label { get; private set; } = "";

    public CustomFieldType Type { get; private set; }

    /// <summary>Seçenekli alanın seçenekleri, satır başına bir tane.</summary>
    public string? OptionsText { get; private set; }

    /// <summary>Yalnızca bu tür kayıtlarda gösterilir; boşsa hepsinde.</summary>
    public ContactType? AppliesTo { get; private set; }

    public string? HelpText { get; private set; }

    public int Order { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyList<string> Options => OptionsText?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    public static Result<CustomField> Create(CustomFieldInput input)
    {
        var field = new CustomField();
        var updated = field.Update(input);
        return updated.IsSuccess ? field : updated.Error!;
    }

    public Result<bool> Update(CustomFieldInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Label) || input.Label.Trim().Length > 100)
        {
            return new Error("custom_fields.label", "Alan adı gerekli (en fazla 100 karakter).");
        }

        var options = input.Options.Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.Create(Turkish, ignoreCase: true)).ToList();
        if (input.Type == CustomFieldType.Choice && options.Count < 2)
        {
            return new Error("custom_fields.options", "Seçenekli alan için en az iki seçenek yazın.");
        }

        if (options.Any(o => o.Length > 100))
        {
            return new Error("custom_fields.options", "Bir seçenek en fazla 100 karakter olabilir.");
        }

        Label = input.Label.Trim();
        Type = input.Type;
        OptionsText = input.Type == CustomFieldType.Choice ? string.Join('\n', options) : null;
        AppliesTo = input.AppliesTo;
        HelpText = string.IsNullOrWhiteSpace(input.HelpText) ? null : input.HelpText.Trim();
        Order = input.Order;
        IsActive = input.IsActive;
        return true;
    }

    public bool AppliesToType(ContactType type) => AppliesTo is null || AppliesTo == type;

    /// <summary>
    /// Girilen değeri saklama biçimine çevirir: sayı "1234.5", tarih "yyyy-MM-dd", evet/hayır "true"/"false".
    /// Boş değer null döner (alan temizlenir).
    /// </summary>
    public Result<string?> Normalize(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return (string?)null;
        }

        switch (Type)
        {
            case CustomFieldType.Text:
                return value.Length > MaxTextLength ? Invalid($"en fazla {MaxTextLength} karakter olabilir") : value;
            case CustomFieldType.Number:
                return TryParseNumber(value, out var number) ? number.ToString(CultureInfo.InvariantCulture) : Invalid("sayı olmalı");
            case CustomFieldType.Date:
                return DateOnly.TryParseExact(value, ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : Invalid("tarih olmalı (gg.aa.yyyy)");
            case CustomFieldType.YesNo:
                return value.ToLower(Turkish) switch
                {
                    "true" or "evet" or "e" or "1" or "var" => "true",
                    "false" or "hayır" or "hayir" or "h" or "0" or "yok" => "false",
                    _ => Invalid("evet veya hayır olmalı"),
                };
            default:
                var option = Options.FirstOrDefault(o => string.Compare(o, value, Turkish, CompareOptions.IgnoreCase) == 0);
                return option ?? Invalid("seçeneklerden biri olmalı");
        }

        Result<string?> Invalid(string reason) => new Error("custom_fields.value", $"\"{Label}\" {reason}.");
    }

    /// <summary>"1.250,5" ve "1250.5" aynı sayıdır; virgül yoksa nokta yalnızca binlik gruplarda ("1.250") ayraç sayılır.</summary>
    public static bool TryParseNumber(string value, out decimal number)
    {
        var turkish = value.Contains(',') || System.Text.RegularExpressions.Regex.IsMatch(value, @"^-?\d{1,3}(\.\d{3})+$");
        return decimal.TryParse(value, NumberStyles.Number, turkish ? Turkish : CultureInfo.InvariantCulture, out number);
    }

    /// <summary>Saklanan değerin ekranda gösterimi.</summary>
    public string Display(string value) => Type switch
    {
        CustomFieldType.Number when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) => n.ToString("#,0.##", Turkish),
        CustomFieldType.Date when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var d) => d.ToString("dd.MM.yyyy", Turkish),
        CustomFieldType.YesNo => value == "true" ? "Evet" : "Hayır",
        _ => value,
    };
}
