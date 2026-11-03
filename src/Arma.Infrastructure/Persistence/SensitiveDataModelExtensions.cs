using System.Reflection;
using Arma.Infrastructure.Security;
using Arma.SharedKernel.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Arma.Infrastructure.Persistence;

public static class SensitiveDataModelExtensions
{
    private static readonly ValueConverter<string, string> Converter = new(
        value => SensitiveDataProtector.Current.Encrypt(value),
        stored => SensitiveDataProtector.Current.Decrypt(stored));

    /// <summary>
    /// <see cref="SensitiveDataAttribute"/> taşıyan metin alanlarını veritabanında şifreli saklar. Şifreli değer
    /// açık değerden uzun olduğu için sütun uzunluk sınırı kaldırılır; uzunluk kuralı alan modelinde kalır.
    /// OnModelCreating'in sonunda çağrılmalıdır.
    /// </summary>
    public static ModelBuilder EncryptSensitiveData(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (property.ClrType == typeof(string) && property.PropertyInfo?.GetCustomAttribute<SensitiveDataAttribute>() is not null)
                {
                    property.SetValueConverter(Converter);
                    property.SetMaxLength(null);
                }
            }
        }

        return modelBuilder;
    }
}
