using System.Globalization;
using Arma.Modules.Comms.Domain;
using Arma.Modules.Comms.Infrastructure;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Comms.Application;

internal sealed class CommsInsights(CommsDbContext db, TimeProvider clock) : IContactCriteriaProvider, IDashboardContributor
{
    private const string Group = "İletişim";

    public Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CriterionDefinition>>(
        [
            new("consent_email", Group, "E-posta ile ticari ileti izni var", CriterionKind.Flag),
            new("consent_sms", Group, "SMS ile ticari ileti izni var", CriterionKind.Flag),
            new("consent_email_rejected", Group, "E-posta iznini reddetti", CriterionKind.Flag),
        ]);

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken) => key switch
    {
        "consent_email" => new(await With(Channel.Email, ConsentStatus.Approved, cancellationToken)),
        "consent_sms" => new(await With(Channel.Sms, ConsentStatus.Approved, cancellationToken)),
        "consent_email_rejected" => new(await With(Channel.Email, ConsentStatus.Rejected, cancellationToken)),
        _ => null,
    };

    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var since = clock.GetUtcNow().AddDays(-30);
        var sent = await db.Messages.CountAsync(m => m.Status == MessageStatus.Sent && m.SentAt >= since, cancellationToken);
        var failed = await db.Messages.CountAsync(m => m.Status == MessageStatus.Failed && m.CreatedAt >= since, cancellationToken);
        return [new("Gönderilen ileti (30 gün)", sent.ToString("N0", CultureInfo.GetCultureInfo("tr-TR")), failed > 0 ? $"{failed} ileti gönderilemedi" : "Hata yok", "comms/mailings", 40, CommsPermissions.Read)];
    }

    private Task<List<Guid>> With(Channel channel, ConsentStatus status, CancellationToken cancellationToken) =>
        db.Consents.Where(c => c.Channel == channel && c.Status == status).Select(c => c.ContactId).ToListAsync(cancellationToken);
}
