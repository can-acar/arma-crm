using Arma.Modules.Forms.Domain;

namespace Arma.Modules.Forms.Web;

internal static class FormLabels
{
    public static string Type(FieldType type) => type switch
    {
        FieldType.Text => "Kısa metin",
        FieldType.Email => "E-posta",
        FieldType.Phone => "Telefon",
        FieldType.TextArea => "Uzun metin",
        FieldType.Select => "Seçim",
        FieldType.Checkbox => "Onay kutusu",
        FieldType.Date => "Tarih",
        FieldType.Number => "Sayı",
        _ => type.ToString(),
    };

    public static string Map(string map) => map switch
    {
        FieldMap.FirstName => "Ad",
        FieldMap.LastName => "Soyad",
        FieldMap.Email => "E-posta",
        FieldMap.Phone => "Telefon",
        _ => map,
    };
}
