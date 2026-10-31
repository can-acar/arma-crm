namespace Arma.SharedKernel.Security;

/// <summary>
/// Hassas alanları (TCKN gibi) veritabanında şifreli tutar. Şifreli değer her seferinde farklı üretildiği için
/// eşitlik araması <see cref="Hash"/> ile üretilen anahtarlı özet (blind index) üzerinden yapılır.
/// </summary>
public interface ISensitiveDataProtector
{
    /// <summary>Değerin anahtarlı özeti; aynı değer her zaman aynı özeti verir, anahtar olmadan geri çevrilemez.</summary>
    string Hash(string value);
}

/// <summary>
/// Bu alanın değeri, adı verilen hassas alanın anahtarlı özetidir; kayıtta otomatik doldurulur.
/// Hassas alan üzerinde eşitlik araması ve tekillik bu özet üzerinden yapılır.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class BlindIndexAttribute(string sourceProperty) : Attribute
{
    public string SourceProperty { get; } = sourceProperty;
}
