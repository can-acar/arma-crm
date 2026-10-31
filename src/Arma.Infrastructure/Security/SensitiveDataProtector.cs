using System.Security.Cryptography;
using System.Text;
using Arma.SharedKernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Arma.Infrastructure.Security;

/// <summary>
/// AES-256-GCM ile alan şifreleme ve HMAC-SHA256 ile blind index. Ana anahtar veritabanında değil
/// yapılandırmada (<c>Arma:Security:EncryptionKey</c>, 32 baytın base64'ü) durur; veritabanı yedeği tek başına
/// ele geçirilse de değerler okunamaz. Şifreleme ve özet için ana anahtardan ayrı alt anahtarlar türetilir.
/// </summary>
public sealed class SensitiveDataProtector : ISensitiveDataProtector
{
    public const string ConfigurationKey = "Arma:Security:EncryptionKey";

    /// <summary>Şifreli değerlerin öneki; önekle başlamayan değer şifrelemeden önceki açık kayıttır.</summary>
    public const string Prefix = "enc1:";

    private const int NonceSize = 12;
    private const int TagSize = 16;

    // Yalnızca geliştirme ortamında, anahtar verilmemişse kullanılır; üretimde anahtar zorunludur.
    private const string DevelopmentKey = "ZGV2ZWxvcG1lbnQtb25seS1hcm1hLWZpZWxkLWtleSE=";

    private static SensitiveDataProtector? current;

    private readonly byte[] encryptionKey;
    private readonly byte[] indexKey;

    public SensitiveDataProtector(byte[] masterKey)
    {
        if (masterKey.Length != 32)
        {
            throw new InvalidOperationException($"'{ConfigurationKey}' 32 baytlık bir anahtarın base64 karşılığı olmalıdır.");
        }

        encryptionKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "arma-field-encryption-v1"u8.ToArray());
        indexKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, 32, info: "arma-blind-index-v1"u8.ToArray());
    }

    /// <summary>
    /// EF değer dönüştürücülerinin kullandığı örnek. Model bir kez kurulup önbelleğe alındığı için dönüştürücü
    /// DI'dan çözülemez; örnek, DI kaydı oluşturulurken <see cref="UseForModel"/> ile atanır.
    /// </summary>
    public static SensitiveDataProtector Current =>
        current ?? throw new InvalidOperationException("Hassas veri şifreleyicisi henüz yapılandırılmadı.");

    public static void UseForModel(SensitiveDataProtector protector) => current = protector;

    public static SensitiveDataProtector Create(IConfiguration configuration, IHostEnvironment environment, ILogger logger)
    {
        var configured = configuration[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"'{ConfigurationKey}' yapılandırması eksik. Kimlik numarası gibi hassas alanlar bu anahtarla şifrelenir; " +
                    "kurulum betiği .env dosyasına üretir (ARMA_ENCRYPTION_KEY). Anahtarı yedekleyin: kaybolursa şifreli alanlar okunamaz.");
            }

            logger.LogWarning("'{Key}' tanımlı değil; geliştirme anahtarı kullanılıyor. Üretimde bu anahtar kabul edilmez.", ConfigurationKey);
            configured = DevelopmentKey;
        }

        byte[] key;
        try
        {
            key = Convert.FromBase64String(configured.Trim());
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"'{ConfigurationKey}' geçerli bir base64 değeri değil.");
        }

        return new SensitiveDataProtector(key);
    }

    public string Encrypt(string plaintext)
    {
        var data = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + data.Length + TagSize];
        var nonce = buffer.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(encryptionKey, TagSize);
        aes.Encrypt(nonce, data, buffer.AsSpan(NonceSize, data.Length), buffer.AsSpan(NonceSize + data.Length, TagSize));
        return Prefix + Convert.ToBase64String(buffer);
    }

    public string Decrypt(string stored)
    {
        // Şifreleme öncesinden kalan açık değer olduğu gibi döner; migrate komutu bunları şifreler.
        if (!IsEncrypted(stored))
        {
            return stored;
        }

        var buffer = Convert.FromBase64String(stored[Prefix.Length..]);
        var length = buffer.Length - NonceSize - TagSize;
        var data = new byte[length];

        using var aes = new AesGcm(encryptionKey, TagSize);
        try
        {
            aes.Decrypt(buffer.AsSpan(0, NonceSize), buffer.AsSpan(NonceSize, length), buffer.AsSpan(NonceSize + length, TagSize), data);
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new CryptographicException($"Şifreli alan çözülemedi. '{ConfigurationKey}' bu veritabanını şifreleyen anahtarla aynı mı?");
        }

        return Encoding.UTF8.GetString(data);
    }

    public string Hash(string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(indexKey, Encoding.UTF8.GetBytes(value)));

    public static bool IsEncrypted(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);
}
