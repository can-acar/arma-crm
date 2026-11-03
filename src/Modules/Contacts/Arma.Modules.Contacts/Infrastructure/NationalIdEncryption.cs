using Arma.Infrastructure.Security;
using Arma.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Infrastructure;

/// <summary>
/// Şifreleme öncesinden kalan açık kimlik numaralarını şifreler ve özetlerini doldurur. <c>migrate</c> komutunda
/// şema güncellemesinden sonra çalışır; yalnızca açık kalan satırlara dokunduğu için tekrar çalıştırmak zararsızdır.
/// Kayıt damgası ve denetim izi yazılmaz: kaydın içeriği değişmez, yalnızca saklanma biçimi değişir.
/// </summary>
internal static class NationalIdEncryption
{
    private const int BatchSize = 500;

    public static async Task<int> EncryptPlaintextAsync(ContactsDbContext db, ISensitiveDataProtector protector, CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            // Row Level Security bu kurulumun kurumuna ait satırları döndürür.
            var ids = await db.Database
                .SqlQuery<Guid>($"""
                    SELECT id AS "Value" FROM contacts.contacts
                    WHERE national_id IS NOT NULL AND national_id NOT LIKE {SensitiveDataProtector.Prefix + "%"}
                    LIMIT {BatchSize}
                    """)
                .ToListAsync(cancellationToken);
            if (ids.Count == 0)
            {
                return total;
            }

            // Dönüştürücü önek taşımayan değeri olduğu gibi döndürür, yazarken şifreler.
            var rows = await db.Contacts.IgnoreQueryFilters().AsNoTracking()
                .Where(c => ids.Contains(c.Id))
                .Select(c => new { c.Id, c.NationalId })
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                var value = row.NationalId!;
                var hash = protector.Hash(value);
                await db.Contacts.IgnoreQueryFilters()
                    .Where(c => c.Id == row.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.NationalId, value).SetProperty(c => c.NationalIdHash, hash), cancellationToken);
            }

            total += rows.Count;
        }
    }
}
