using Arma.Infrastructure.Messaging;
using System.Globalization;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Invoicing.Contracts;
using Arma.Modules.Membership.Domain;
using Arma.Modules.Membership.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Membership.Application;

public sealed record MembershipTypeView(Guid Id, string Name, string? Description, int DurationMonths, decimal Fee, int GraceDays, bool IsActive, int ActiveMembers);

public sealed record SaveMembershipTypeRequest(string Name, string? Description, int DurationMonths, decimal Fee, int GraceDays, bool IsActive);

public sealed record MemberView(
    Guid Id,
    Guid ContactId,
    string ContactName,
    long Number,
    Guid TypeId,
    string TypeName,
    DateOnly JoinedOn,
    DateOnly StartOn,
    DateOnly? EndOn,
    MembershipStatus Status,
    DateOnly? CancelledOn,
    string? CancelReason,
    string? Note,
    decimal Fee,
    bool IsLifetime);

public sealed record DuesView(Guid Id, Guid MemberId, decimal Amount, DateOnly PaidOn, DuesMethod Method, DateOnly PeriodStart, DateOnly? PeriodEnd, string? Note);

/// <param name="Amount">Boşsa aidat alınmadan kaydedilir.</param>
public sealed record DuesInput(decimal Amount, DateOnly PaidOn, DuesMethod Method, string? Note);

public sealed record JoinRequest(Guid ContactId, Guid TypeId, DateOnly StartOn, string? Note, DuesInput? Dues);

public sealed record MemberFilter(MembershipStatus? Status = null, Guid? TypeId = null, Guid? ContactId = null, DateOnly? EndsBefore = null, int Page = 1, int PageSize = 50);

public sealed record MemberPage(IReadOnlyList<MemberView> Items, int Total);

public static class MembershipErrors
{
    public static readonly Error NotFound = new("memberships.not_found", "Üyelik bulunamadı.");
    public static readonly Error TypeNotFound = new("membership_types.not_found", "Üyelik türü bulunamadı.");
    public static readonly Error TypeInactive = new("membership_types.inactive", "Pasif üyelik türüne yeni üye kaydedilemez.");
    public static readonly Error ContactNotFound = new("memberships.contact", "Kişi bulunamadı.");
    public static readonly Error AlreadyMember = new("memberships.duplicate", "Kişinin bu türde devam eden bir üyeliği var.");
    public static readonly Error DuplicateType = new("membership_types.duplicate", "Bu adla bir üyelik türü zaten var.");
}

public sealed class MembershipService(MembershipDbContext db, IContactDirectory contacts, IDbContextOutbox outbox, TimeProvider clock)
{
    internal const string SourceModule = "Membership";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IReadOnlyList<MembershipTypeView>> TypesAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var today = Today;
        var types = db.Types.AsNoTracking();
        if (activeOnly)
        {
            types = types.Where(t => t.IsActive);
        }

        return await types.OrderBy(t => t.Name)
            .Select(t => new MembershipTypeView(t.Id, t.Name, t.Description, t.DurationMonths, t.Fee, t.GraceDays, t.IsActive,
                db.Members.Count(m => m.TypeId == t.Id && m.CancelledOn == null && m.StartOn <= today && (m.EndOn == null || m.EndOn >= today))))
            .ToListAsync(cancellationToken);
    }

    public async Task<Result<Guid>> SaveTypeAsync(Guid? id, SaveMembershipTypeRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? "";
        if (await db.Types.AnyAsync(t => t.Name == name && t.Id != id, cancellationToken))
        {
            return MembershipErrors.DuplicateType;
        }

        MembershipType type;
        if (id is null)
        {
            var created = MembershipType.Create(request.Name ?? "", request.Description, request.DurationMonths, request.Fee, request.GraceDays);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            type = created.Value!;
            db.Types.Add(type);
        }
        else
        {
            var found = await db.Types.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
            if (found is null)
            {
                return MembershipErrors.TypeNotFound;
            }

            type = found;

            var updated = type.Update(request.Name ?? "", request.Description, request.DurationMonths, request.Fee, request.GraceDays, request.IsActive);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return type.Id;
    }

    public async Task<MemberPage> ListAsync(MemberFilter filter, CancellationToken cancellationToken = default)
    {
        var query = Query(filter);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.Member.Number)
            .Skip((Math.Max(filter.Page, 1) - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        return new(await ToViewsAsync(rows, cancellationToken), total);
    }

    public async Task<IReadOnlyList<MemberView>> ForContactAsync(Guid contactId, CancellationToken cancellationToken = default) =>
        (await ListAsync(new MemberFilter(ContactId: contactId, PageSize: 100), cancellationToken)).Items;

    public async Task<MemberView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await db.Members.AsNoTracking().Where(m => m.Id == id)
            .Join(db.Types, m => m.TypeId, t => t.Id, (m, t) => new Row { Member = m, Type = t }).ToListAsync(cancellationToken);
        return (await ToViewsAsync(rows, cancellationToken)).FirstOrDefault();
    }

    public async Task<IReadOnlyList<DuesView>> PaymentsAsync(Guid memberId, CancellationToken cancellationToken = default) =>
        await db.Payments.AsNoTracking().Where(p => p.MemberId == memberId).OrderByDescending(p => p.PaidOn)
            .Select(p => new DuesView(p.Id, p.MemberId, p.Amount, p.PaidOn, p.Method, p.PeriodStart, p.PeriodEnd, p.Note))
            .ToListAsync(cancellationToken);

    /// <summary>Üye kaydeder, sıradaki üye numarasını verir; aidat girildiyse ilk dönemin ödemesi olarak yazar.</summary>
    public async Task<Result<Guid>> JoinAsync(JoinRequest request, CancellationToken cancellationToken = default)
    {
        var type = await db.Types.FirstOrDefaultAsync(t => t.Id == request.TypeId, cancellationToken);
        if (type is null)
        {
            return MembershipErrors.TypeNotFound;
        }

        if (!type.IsActive)
        {
            return MembershipErrors.TypeInactive;
        }

        var contact = await contacts.FindAsync(request.ContactId, cancellationToken);
        if (contact is null)
        {
            return MembershipErrors.ContactNotFound;
        }

        var today = Today;
        var existing = await db.Members.Where(m => m.ContactId == request.ContactId && m.TypeId == type.Id).ToListAsync(cancellationToken);
        if (existing.Any(m => Member.StatusOf(m.CancelledOn, m.StartOn, m.EndOn, type.GraceDays, today) is not (MembershipStatus.Expired or MembershipStatus.Cancelled)))
        {
            return MembershipErrors.AlreadyMember;
        }

        var member = Member.Create(contact.Id, type, await db.NextMemberNumberAsync(cancellationToken), request.StartOn, request.Note);
        db.Members.Add(member);
        var dues = AddDues(member, request.Dues, member.StartOn, member.EndOn);
        if (dues.Error is not null)
        {
            return dues.Error;
        }

        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(member, $"Üye oldu: {type.Name} (No {member.Number})", PeriodText(member.StartOn, member.EndOn), member.StartOn, $"{member.Id}:join"));
        await PublishDuesTimelineAsync(member, dues.Payment);
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return member.Id;
    }

    /// <summary>Bir dönem uzatır ve (girildiyse) aidatı o döneme yazar.</summary>
    public async Task<Result<Guid>> RenewAsync(Guid id, DuesInput? dues, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return MembershipErrors.NotFound;
        }

        var type = await db.Types.FirstAsync(t => t.Id == member.TypeId, cancellationToken);
        var renewed = member.Renew(type, Today);
        if (!renewed.IsSuccess)
        {
            return renewed.Error!;
        }

        var (start, end) = renewed.Value;
        var payment = AddDues(member, dues, start, end);
        if (payment.Error is not null)
        {
            return payment.Error;
        }

        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(member, $"Üyelik yenilendi: {type.Name}", PeriodText(start, end), Today, $"{member.Id}:renew:{start:yyyyMMdd}"));
        await PublishDuesTimelineAsync(member, payment.Payment);
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return member.Id;
    }

    /// <summary>Dönemi değiştirmeden aidat ödemesi alır (taksitli ödeme, geçmiş borç gibi).</summary>
    public async Task<Result<Guid>> RecordDuesAsync(Guid id, DuesInput dues, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return MembershipErrors.NotFound;
        }

        var (payment, error) = AddDues(member, dues, member.StartOn, member.EndOn);
        if (error is not null)
        {
            return error;
        }

        outbox.Enroll(db);
        await PublishDuesTimelineAsync(member, payment);
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return payment!.Id;
    }

    public async Task<Result<Guid>> CancelAsync(Guid id, DateOnly on, string? reason, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return MembershipErrors.NotFound;
        }

        var cancelled = member.Cancel(on, reason);
        if (!cancelled.IsSuccess)
        {
            return cancelled.Error!;
        }

        var typeName = await db.Types.Where(t => t.Id == member.TypeId).Select(t => t.Name).FirstAsync(cancellationToken);
        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(member, $"Üyelik sonlandırıldı: {typeName}", member.CancelReason, on, $"{member.Id}:cancel:{on:yyyyMMdd}"));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, Guid typeId, string? note, CancellationToken cancellationToken = default)
    {
        var member = await db.Members.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (member is null)
        {
            return MembershipErrors.NotFound;
        }

        var type = await db.Types.FirstOrDefaultAsync(t => t.Id == typeId, cancellationToken);
        if (type is null)
        {
            return MembershipErrors.TypeNotFound;
        }

        member.ChangeType(type);
        member.UpdateNote(note);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<bool> DeletePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);
        if (payment is null)
        {
            return false;
        }

        db.Payments.Remove(payment);
        outbox.Enroll(db);
        await outbox.PublishAsync(new RemoveTimelineEntry(SourceModule, $"dues:{payment.Id}"));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return true;
    }

    /// <summary>Aidat ödemeleri listesi; tarih aralığı ve toplam için.</summary>
    public async Task<(IReadOnlyList<(DuesView Payment, MemberView Member)> Items, decimal Total)> PaymentsBetweenAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var payments = await db.Payments.AsNoTracking().Where(p => p.PaidOn >= from && p.PaidOn <= to).OrderByDescending(p => p.PaidOn)
            .Select(p => new DuesView(p.Id, p.MemberId, p.Amount, p.PaidOn, p.Method, p.PeriodStart, p.PeriodEnd, p.Note))
            .ToListAsync(cancellationToken);
        var memberIds = payments.Select(p => p.MemberId).Distinct().ToList();
        var rows = await db.Members.AsNoTracking().Where(m => memberIds.Contains(m.Id))
            .Join(db.Types, m => m.TypeId, t => t.Id, (m, t) => new Row { Member = m, Type = t }).ToListAsync(cancellationToken);
        var members = (await ToViewsAsync(rows, cancellationToken)).ToDictionary(m => m.Id);
        return ([.. payments.Select(p => (p, members[p.MemberId]))], payments.Sum(p => p.Amount));
    }

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        foreach (var member in await db.Members.Where(m => m.ContactId == from).ToListAsync(cancellationToken))
        {
            member.MoveTo(to);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    internal IQueryable<Row> Query(MemberFilter filter)
    {
        var today = Today;
        var query = db.Members.AsNoTracking().Join(db.Types, m => m.TypeId, t => t.Id, (m, t) => new Row { Member = m, Type = t });
        if (filter.TypeId is { } typeId)
        {
            query = query.Where(x => x.Member.TypeId == typeId);
        }

        if (filter.ContactId is { } contactId)
        {
            query = query.Where(x => x.Member.ContactId == contactId);
        }

        if (filter.EndsBefore is { } endsBefore)
        {
            query = query.Where(x => x.Member.EndOn != null && x.Member.EndOn <= endsBefore);
        }

        return filter.Status switch
        {
            MembershipStatus.Cancelled => query.Where(x => x.Member.CancelledOn != null && x.Member.CancelledOn <= today),
            MembershipStatus.Pending => query.Where(x => (x.Member.CancelledOn == null || x.Member.CancelledOn > today) && x.Member.StartOn > today),
            MembershipStatus.Current => query.Where(x => (x.Member.CancelledOn == null || x.Member.CancelledOn > today) && x.Member.StartOn <= today
                && (x.Member.EndOn == null || x.Member.EndOn >= today)),
            MembershipStatus.Grace => query.Where(x => (x.Member.CancelledOn == null || x.Member.CancelledOn > today) && x.Member.EndOn < today
                && x.Member.EndOn!.Value.AddDays(x.Type.GraceDays) >= today),
            MembershipStatus.Expired => query.Where(x => (x.Member.CancelledOn == null || x.Member.CancelledOn > today)
                && x.Member.EndOn!.Value.AddDays(x.Type.GraceDays) < today),
            _ => query,
        };
    }

    /// <summary>Sorgu satırı; EF'in çevirebilmesi için kurucu yerine özellik atamasıyla oluşturulur.</summary>
    internal sealed class Row
    {
        public required Member Member { get; init; }

        public required MembershipType Type { get; init; }
    }

    private async Task<IReadOnlyList<MemberView>> ToViewsAsync(IReadOnlyList<Row> rows, CancellationToken cancellationToken)
    {
        var names = await contacts.FindManyAsync([.. rows.Select(r => r.Member.ContactId).Distinct()], cancellationToken);
        var today = Today;
        return [.. rows.Select(r => new MemberView(r.Member.Id, r.Member.ContactId, names.TryGetValue(r.Member.ContactId, out var c) ? c.DisplayName : "(silinmiş kişi)",
            r.Member.Number, r.Type.Id, r.Type.Name, r.Member.JoinedOn, r.Member.StartOn, r.Member.EndOn,
            Member.StatusOf(r.Member.CancelledOn, r.Member.StartOn, r.Member.EndOn, r.Type.GraceDays, today),
            r.Member.CancelledOn, r.Member.CancelReason, r.Member.Note, r.Type.Fee, r.Type.IsLifetime))];
    }

    private (DuesPayment? Payment, Error? Error) AddDues(Member member, DuesInput? input, DateOnly periodStart, DateOnly? periodEnd)
    {
        if (input is null)
        {
            return (null, null);
        }

        var created = DuesPayment.Create(member.Id, input.Amount, input.PaidOn, input.Method, periodStart, periodEnd, input.Note);
        if (!created.IsSuccess)
        {
            return (null, created.Error);
        }

        db.Payments.Add(created.Value!);
        return (created.Value, null);
    }

    private async Task PublishDuesTimelineAsync(Member member, DuesPayment? payment)
    {
        if (payment is not null)
        {
            await outbox.PublishAsync(Timeline(member, $"Aidat alındı: {payment.Amount.ToString("N2", Turkish)} TL", PeriodText(payment.PeriodStart, payment.PeriodEnd),
                payment.PaidOn, $"dues:{payment.Id}"));

            // Fatura modülü ayarlarına göre aidat faturası açar. Alınan tutar KDV dahildir.
            await outbox.PublishAsync(new InvoiceRequested(InvoiceSources.Dues, payment.Id, $"{member.Number} numaralı üye aidatı", member.ContactId, payment.PaidOn,
                [new InvoiceRequestLine($"Üyelik aidatı ({PeriodText(payment.PeriodStart, payment.PeriodEnd)})", 1, null, payment.Amount, 0, null) { VatIncluded = true }],
                null));
        }
    }

    /// <summary>Eski sistemden aktarılan üyelikleri zaman çizelgesi kayıtlarıyla birlikte kaydeder.</summary>
    internal async Task SaveImportedAsync(IReadOnlyList<(Member Member, MembershipType Type)> members, CancellationToken cancellationToken)
    {
        outbox.Enroll(db);
        foreach (var (member, type) in members)
        {
            db.Members.Add(member);
            await outbox.PublishAsync(Timeline(member, $"Üye oldu: {type.Name} (No {member.Number})", PeriodText(member.StartOn, member.EndOn), member.JoinedOn, $"{member.Id}:join"));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        db.ChangeTracker.Clear();
    }

    private RecordTimelineEntry Timeline(Member member, string subject, string? body, DateOnly on, string sourceId)
    {
        var now = clock.GetLocalNow();
        var at = DateOnly.FromDateTime(now.DateTime) == on ? now : new DateTimeOffset(on.ToDateTime(TimeOnly.MinValue), now.Offset);
        return new RecordTimelineEntry([member.ContactId], ActivityKind.System, subject, body, at, SourceModule, sourceId);
    }

    internal static string PeriodText(DateOnly start, DateOnly? end) =>
        end is null ? $"{start:dd.MM.yyyy} tarihinden itibaren, süresiz" : $"Dönem {start:dd.MM.yyyy} – {end:dd.MM.yyyy}";
}
