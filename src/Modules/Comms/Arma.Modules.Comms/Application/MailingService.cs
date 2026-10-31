using Arma.Infrastructure.Messaging;
using Arma.Modules.Comms.Domain;
using Arma.Modules.Comms.Infrastructure;
using Arma.Modules.Contacts.Contracts;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Comms.Application;

/// <summary>Toplu iletinin alıcılarını oluşturur ve her birini kuyruğa alır.</summary>
public sealed record ExpandMailing(Guid MailingId);

public sealed record MailingView(
    Guid Id, string Name, Channel Channel, string? Subject, string Body, bool IsCommercial, Guid? GroupId, Guid? TagId,
    MailingStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    int Pending, int Sent, int Failed, int Skipped);

public sealed record SaveMailingRequest(string Name, Channel Channel, string? Subject, string Body, bool IsCommercial, Guid? GroupId, Guid? TagId);

/// <summary>Gönderim öncesi özet: hedef kitlede kaç kişi var, kaçına gidebilir.</summary>
public sealed record AudiencePreview(int Total, int Reachable, int NoAddress, int NoConsent);

public sealed record SaveTemplateRequest(string Name, Channel Channel, string? Subject, string Body);

public sealed record TemplateView(Guid Id, string Name, Channel Channel, string? Subject, string Body);

public sealed class MailingService(CommsDbContext db, IContactAudiences audiences, ConsentService consents, IDbContextOutbox outbox, TimeProvider clock)
{
    public async Task<IReadOnlyList<MailingView>> ListAsync(CancellationToken cancellationToken = default) =>
        await Project(db.Mailings.AsNoTracking().OrderByDescending(m => m.CreatedAt)).ToListAsync(cancellationToken);

    public async Task<MailingView?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Project(db.Mailings.AsNoTracking().Where(m => m.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MessageView>> RecipientsAsync(Guid mailingId, CancellationToken cancellationToken = default) =>
        await db.Messages.AsNoTracking().Where(m => m.MailingId == mailingId).OrderBy(m => m.Status).ThenBy(m => m.To)
            .Select(m => new MessageView(m.Id, m.ContactId, m.MailingId, m.Channel, m.To, m.Subject, m.Body, m.IsCommercial, m.Status, m.Error, m.CreatedAt, m.SentAt, m.CreatedBy))
            .ToListAsync(cancellationToken);

    public async Task<Result<Guid>> SaveAsync(Guid? id, SaveMailingRequest request, CancellationToken cancellationToken = default)
    {
        if (id is null)
        {
            var created = Mailing.Create(request.Name, request.Channel, request.Subject, request.Body, request.IsCommercial, request.GroupId, request.TagId);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            db.Mailings.Add(created.Value!);
            await db.SaveChangesAsync(cancellationToken);
            return created.Value!.Id;
        }

        var mailing = await db.Mailings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (mailing is null)
        {
            return new Error("mailings.not_found", "İleti bulunamadı.");
        }

        var updated = mailing.Update(request.Name, request.Channel, request.Subject, request.Body, request.IsCommercial, request.GroupId, request.TagId);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return mailing.Id;
    }

    public async Task<AudiencePreview> PreviewAsync(Channel channel, bool commercial, Guid? groupId, Guid? tagId, CancellationToken cancellationToken = default)
    {
        var members = await audiences.MembersAsync(groupId, tagId, cancellationToken);
        var withAddress = members.Where(m => MessageService.AddressOf(m, channel) is not null).ToList();
        var approved = commercial ? await consents.ApprovedAsync(channel, withAddress.Select(m => m.Id).ToList(), cancellationToken) : null;
        var reachable = approved is null ? withAddress.Count : withAddress.Count(m => approved.Contains(m.Id));
        return new AudiencePreview(members.Count, reachable, members.Count - withAddress.Count, withAddress.Count - reachable);
    }

    public async Task<Result<Guid>> StartAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var mailing = await db.Mailings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (mailing is null)
        {
            return new Error("mailings.not_found", "İleti bulunamadı.");
        }

        var started = mailing.Start(clock.GetUtcNow());
        if (!started.IsSuccess)
        {
            return started.Error!;
        }

        outbox.Enroll(db);
        await outbox.PublishAsync(new ExpandMailing(id));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return id;
    }

    /// <summary>Her alıcı için ayrı ileti oluşturur; adresi veya izni olmayanlar "gönderilmedi" olarak kaydedilir.</summary>
    internal async Task ExpandAsync(Guid id, CancellationToken cancellationToken)
    {
        var mailing = await db.Mailings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (mailing is null || mailing.Status != MailingStatus.Sending || await db.Messages.AnyAsync(m => m.MailingId == id, cancellationToken))
        {
            return;
        }

        var members = await audiences.MembersAsync(mailing.GroupId, mailing.TagId, cancellationToken);
        var approved = mailing.IsCommercial ? await consents.ApprovedAsync(mailing.Channel, members.Select(m => m.Id).ToList(), cancellationToken) : null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queued = new List<OutboundMessage>();
        foreach (var member in members)
        {
            var to = MessageService.AddressOf(member, mailing.Channel);
            var message = OutboundMessage.Create(member.Id, mailing.Id, mailing.Channel, to,
                mailing.Subject is null ? null : MessageText.Merge(mailing.Subject, member), MessageText.Merge(mailing.Body, member), mailing.IsCommercial);
            db.Messages.Add(message);
            if (to is null)
            {
                message.Skip("Adres yok");
            }
            else if (approved is not null && !approved.Contains(member.Id))
            {
                message.Skip("İleti izni yok");
            }
            else if (!seen.Add(to))
            {
                message.Skip("Aynı adrese ikinci ileti");
            }
            else
            {
                queued.Add(message);
            }
        }

        if (queued.Count == 0)
        {
            mailing.Complete(clock.GetUtcNow());
        }

        outbox.Enroll(db);
        foreach (var message in queued)
        {
            await outbox.PublishAsync(new DeliverMessage(message.Id));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
    }

    /// <summary>Bekleyen ileti kalmadıysa gönderimi tamamlandı olarak işaretler.</summary>
    internal async Task CompleteIfDoneAsync(Guid messageId, CancellationToken cancellationToken)
    {
        var mailingId = await db.Messages.Where(m => m.Id == messageId).Select(m => m.MailingId).FirstOrDefaultAsync(cancellationToken);
        if (mailingId is not { } id || await db.Messages.AnyAsync(m => m.MailingId == id && m.Status == MessageStatus.Pending, cancellationToken))
        {
            return;
        }

        var mailing = await db.Mailings.FirstOrDefaultAsync(m => m.Id == id && m.Status == MailingStatus.Sending, cancellationToken);
        if (mailing is not null)
        {
            mailing.Complete(clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<TemplateView>> TemplatesAsync(Channel? channel = null, CancellationToken cancellationToken = default) =>
        await db.Templates.AsNoTracking().Where(t => channel == null || t.Channel == channel).OrderBy(t => t.Name)
            .Select(t => new TemplateView(t.Id, t.Name, t.Channel, t.Subject, t.Body))
            .ToListAsync(cancellationToken);

    public async Task<Result<Guid>> SaveTemplateAsync(Guid? id, SaveTemplateRequest request, CancellationToken cancellationToken = default)
    {
        MessageTemplate? template;
        if (id is null)
        {
            var created = MessageTemplate.Create(request.Name, request.Channel, request.Subject, request.Body);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            template = created.Value!;
            db.Templates.Add(template);
        }
        else
        {
            template = await db.Templates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (template is null)
            {
                return new Error("templates.not_found", "Şablon bulunamadı.");
            }

            var updated = template.Update(request.Name, request.Channel, request.Subject, request.Body);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return template.Id;
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var template = await db.Templates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (template is null)
        {
            return false;
        }

        db.Templates.Remove(template);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private IQueryable<MailingView> Project(IQueryable<Mailing> query) =>
        query.Select(m => new MailingView(m.Id, m.Name, m.Channel, m.Subject, m.Body, m.IsCommercial, m.GroupId, m.TagId, m.Status, m.CreatedAt, m.StartedAt, m.CompletedAt,
            db.Messages.Count(x => x.MailingId == m.Id && x.Status == MessageStatus.Pending),
            db.Messages.Count(x => x.MailingId == m.Id && x.Status == MessageStatus.Sent),
            db.Messages.Count(x => x.MailingId == m.Id && x.Status == MessageStatus.Failed),
            db.Messages.Count(x => x.MailingId == m.Id && x.Status == MessageStatus.Skipped)));
}
