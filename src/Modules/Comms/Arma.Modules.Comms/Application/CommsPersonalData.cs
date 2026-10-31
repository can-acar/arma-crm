using Arma.Modules.Comms.Infrastructure;
using Arma.Modules.Comms.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Comms.Application;

internal sealed class CommsPersonalData(CommsDbContext db) : IPersonalDataSource
{
    public int Order => 300;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("İletişim", "Ticari ileti izni (e-posta, SMS, arama) ve izin adresi", "Pazarlama", "İzinli ticari ileti, İYS bildirimi", "Açık rıza (6563 s. Kanun)", "İzin geri alındıktan sonra 3 yıl"),
        new("İletişim", "Gönderilen e-posta ve SMS içerikleri", "İletişim", "Gönderim kaydı ve şikâyetlere yanıt", "Meşru menfaat", "3 yıl"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var consents = await db.Consents.AsNoTracking().Where(c => c.ContactId == contactId).ToListAsync(cancellationToken);
        var messages = await db.Messages.AsNoTracking().Where(m => m.ContactId == contactId).OrderBy(m => m.CreatedAt).ToListAsync(cancellationToken);
        return
        [
            new("Ticari ileti izinleri", [.. consents.Select(c => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Kanal"] = CommsLabels.Channel(c.Channel),
                ["Durum"] = CommsLabels.Consent(c.Status),
                ["Adres"] = c.Address,
                ["Kaynak"] = CommsLabels.Source(c.Source),
                ["Tarih"] = CommsLabels.DateTime(c.RecordedAt),
            })]),
            new("Gönderilen iletiler", [.. messages.Select(m => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Tarih"] = CommsLabels.DateTime(m.SentAt ?? m.CreatedAt),
                ["Kanal"] = CommsLabels.Channel(m.Channel),
                ["Alıcı"] = m.To,
                ["Konu"] = m.Subject,
                ["Metin"] = m.Body,
                ["Durum"] = CommsLabels.Status(m.Status),
            })]),
        ];
    }

    public async Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken)
    {
        await db.Consents.Where(c => c.ContactId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Address, (string?)null), cancellationToken);
        await db.Messages.Where(m => m.ContactId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.To, (string?)null).SetProperty(m => m.Subject, (string?)null)
                .SetProperty(m => m.Body, "[anonimleştirildi]").SetProperty(m => m.Error, (string?)null), cancellationToken);
    }
}
