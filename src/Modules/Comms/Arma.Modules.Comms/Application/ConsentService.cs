using System.Security.Cryptography;
using Arma.Modules.Comms.Domain;
using Arma.Modules.Comms.Infrastructure;
using Arma.Modules.Comms.Senders;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Comms.Application;

public sealed record ConsentView(Channel Channel, ConsentStatus Status, string? Address, string? Source, DateTimeOffset? RecordedAt, IysState IysState, string? IysError);

/// <summary>İYS'ye bildirilmeyi bekleyen izin.</summary>
public sealed record SyncConsentToIys(Guid ConsentId);

/// <summary>Ticari ileti izinleri ve abonelik iptali.</summary>
public sealed class ConsentService(
    CommsDbContext db,
    IContactAudiences audiences,
    IIysClient iys,
    IDbContextOutbox outbox,
    IDataProtectionProvider protection,
    TimeProvider clock)
{
    public static readonly Channel[] Channels = [Channel.Email, Channel.Sms, Channel.Call];

    private readonly IDataProtector unsubscribeProtector = protection.CreateProtector("Arma.Comms.Unsubscribe");

    public async Task<IReadOnlyList<ConsentView>> ForContactAsync(Guid contactId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Consents.AsNoTracking().Where(c => c.ContactId == contactId).ToListAsync(cancellationToken);
        return Channels.Select(channel => rows.FirstOrDefault(r => r.Channel == channel) is { } c
                ? new ConsentView(channel, c.Status, c.Address, c.Source, c.RecordedAt, c.IysState, c.IysError)
                : new ConsentView(channel, ConsentStatus.Unknown, null, null, null, IysState.NotRequired, null))
            .ToList();
    }

    /// <summary>Ticari ileti için izni onaylı kişiler.</summary>
    public async Task<HashSet<Guid>> ApprovedAsync(Channel channel, IReadOnlyCollection<Guid> contactIds, CancellationToken cancellationToken = default) =>
        (await db.Consents.AsNoTracking()
            .Where(c => c.Channel == channel && c.Status == ConsentStatus.Approved && contactIds.Contains(c.ContactId))
            .Select(c => c.ContactId)
            .ToListAsync(cancellationToken)).ToHashSet();

    public async Task<Result<Guid>> SetAsync(Guid contactId, Channel channel, ConsentStatus status, string source, CancellationToken cancellationToken = default)
    {
        var contacts = await audiences.FindManyAsync([contactId], cancellationToken);
        if (!contacts.TryGetValue(contactId, out var contact))
        {
            return new Error("consents.contact_not_found", "Kişi bulunamadı.");
        }

        var address = channel == Channel.Email ? contact.Email : MessageText.MobileNumber(contact.Phone);
        if (status == ConsentStatus.Approved && address is null)
        {
            return new Error("consents.no_address", channel == Channel.Email
                ? "Kişinin e-posta adresi yok; önce adresi kaydedin."
                : "Kişinin geçerli bir cep telefonu yok; önce numarayı kaydedin.");
        }

        var consent = await db.Consents.FirstOrDefaultAsync(c => c.ContactId == contactId && c.Channel == channel, cancellationToken);
        if (consent is null)
        {
            consent = ChannelConsent.Create(contactId, channel);
            db.Consents.Add(consent);
        }

        consent.Set(status, address, source, clock.GetUtcNow(), iys.Enabled);
        outbox.Enroll(db);
        if (consent.IysState == IysState.Pending)
        {
            await outbox.PublishAsync(new SyncConsentToIys(consent.Id));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);
        return consent.Id;
    }

    /// <summary>İzni İYS'ye bildirir; hata durumunda kayıt "başarısız" kalır ve ekrandan yeniden denenebilir.</summary>
    internal async Task SyncAsync(Guid consentId, CancellationToken cancellationToken)
    {
        var consent = await db.Consents.FirstOrDefaultAsync(c => c.Id == consentId, cancellationToken);
        if (consent is null || consent.IysState is not (IysState.Pending or IysState.Failed) || consent.Address is null)
        {
            return;
        }

        var recipient = consent.Channel == Channel.Email ? consent.Address : "+90" + consent.Address;
        var source = consent.Source switch
        {
            "form" or "unsubscribe" => "HS_WEB",
            "import" => "HS_EORTAM",
            _ => "HS_FIZIKSEL_ORTAM",
        };
        var result = await iys.PushAsync(new IysConsent(recipient, consent.Channel == Channel.Email, consent.Status == ConsentStatus.Approved,
            consent.Channel == Channel.Call, consent.RecordedAt, source), cancellationToken);
        consent.MarkIys(result.Success, result.Error);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RetryIysAsync(CancellationToken cancellationToken = default)
    {
        var failed = await db.Consents.Where(c => c.IysState == IysState.Failed).Select(c => c.Id).ToListAsync(cancellationToken);
        foreach (var id in failed)
        {
            await SyncAsync(id, cancellationToken);
        }

        return failed.Count;
    }

    public string UnsubscribeToken(Guid contactId, Channel channel) =>
        unsubscribeProtector.Protect($"{contactId:N}|{(int)channel}");

    public (Guid ContactId, Channel Channel)? ReadUnsubscribeToken(string? token)
    {
        try
        {
            var parts = unsubscribeProtector.Unprotect(token ?? "").Split('|');
            return Guid.TryParseExact(parts[0], "N", out var id) && int.TryParse(parts[1], out var channel) ? (id, (Channel)channel) : null;
        }
        catch (Exception ex) when (ex is CryptographicException or IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>E-postadaki bağlantıyla gelen iptal: ilgili kanalın izni "ret" olur ve İYS'ye bildirilir.</summary>
    public async Task<bool> UnsubscribeAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (ReadUnsubscribeToken(token) is not { } target)
        {
            return false;
        }

        var current = await db.Consents.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ContactId == target.ContactId && c.Channel == target.Channel, cancellationToken);
        if (current?.Status == ConsentStatus.Rejected)
        {
            return true;
        }

        return (await SetAsync(target.ContactId, target.Channel, ConsentStatus.Rejected, "unsubscribe", cancellationToken)).IsSuccess;
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        var primary = await db.Consents.Where(c => c.ContactId == to).ToListAsync(cancellationToken);
        foreach (var consent in await db.Consents.Where(c => c.ContactId == from).ToListAsync(cancellationToken))
        {
            // Asıl kayıtta aynı kanal için izin varsa o geçerli kalır; mükerrerinki silinir.
            if (primary.Any(p => p.Channel == consent.Channel))
            {
                db.Consents.Remove(consent);
            }
            else
            {
                consent.ChangeContact(to);
            }
        }

        foreach (var message in await db.Messages.Where(m => m.ContactId == from).ToListAsync(cancellationToken))
        {
            message.ChangeContact(to);
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
