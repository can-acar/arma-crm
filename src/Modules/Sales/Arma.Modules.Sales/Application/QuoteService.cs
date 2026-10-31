using Arma.Modules.Activities.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Sales.Domain;
using Arma.Modules.Sales.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Sales.Application;

public sealed record QuoteLineView(int Position, string Description, decimal Quantity, string? Unit, decimal UnitPrice, decimal DiscountRate, int VatRate, decimal Net, decimal Vat);

public sealed record QuoteView(
    Guid Id, string Number, Guid ContactId, string ContactName, Guid? OpportunityId, string? OpportunityTitle, DateOnly IssuedOn, DateOnly ValidUntil,
    QuoteStatus Status, string? Terms, IReadOnlyList<QuoteLineView> Lines, decimal Subtotal, decimal VatTotal, decimal Total)
{
    public bool Expired(DateOnly today) => Status is QuoteStatus.Draft or QuoteStatus.Sent && ValidUntil < today;
}

public static class QuoteErrors
{
    public static readonly Error NotFound = new("quotes.not_found", "Teklif bulunamadı.");
    public static readonly Error OnlyDraftDeletable = new("quotes.not_draft", "Yalnızca taslak teklif silinebilir.");
}

public sealed class QuoteService(SalesDbContext db, SalesService sales, IContactDirectory contacts, IDbContextOutbox outbox, TimeProvider clock)
{
    public async Task<IReadOnlyList<QuoteView>> ListAsync(QuoteStatus? status = null, Guid? contactId = null, Guid? opportunityId = null, CancellationToken cancellationToken = default)
    {
        var query = db.Quotes.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(q => q.Status == s);
        }

        if (contactId is { } c)
        {
            query = query.Where(q => q.ContactId == c);
        }

        if (opportunityId is { } o)
        {
            query = query.Where(q => q.OpportunityId == o);
        }

        return await ToViewsAsync(await query.OrderByDescending(q => q.IssuedOn).ThenByDescending(q => q.Number).Take(500).ToListAsync(cancellationToken), cancellationToken);
    }

    public async Task<QuoteView?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await db.Quotes.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        return quote is null ? null : (await ToViewsAsync([quote], cancellationToken))[0];
    }

    public async Task<Result<Guid>> SaveAsync(Guid? id, QuoteInput input, CancellationToken cancellationToken = default)
    {
        if (await contacts.FindAsync(input.ContactId, cancellationToken) is null)
        {
            return SalesErrors.ContactNotFound;
        }

        if (input.OpportunityId is { } opportunityId && !await db.Opportunities.AnyAsync(o => o.Id == opportunityId, cancellationToken))
        {
            return SalesErrors.NotFound;
        }

        if (id is null)
        {
            var year = input.IssuedOn.Year;
            var number = $"T{year}-{await db.NextQuoteNumberAsync(year, cancellationToken):D4}";
            var created = Quote.Create(number, input);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            db.Quotes.Add(created.Value!);
            await db.SaveChangesAsync(cancellationToken);
            return created.Value!.Id;
        }

        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        var updated = quote.Update(input);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return quote.Id;
    }

    /// <summary>
    /// Teklif durumunu değiştirir. Gönderilen ve sonuçlanan teklif kişinin zaman çizelgesine düşer; bağlı fırsat
    /// varsa kabul edilen teklif fırsatı "kazanıldı", reddedilen "kaybedildi" aşamasına taşır.
    /// </summary>
    public async Task<Result<Guid>> SetStatusAsync(Guid id, QuoteStatus status, CancellationToken cancellationToken = default)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        var changed = quote.SetStatus(status);
        if (!changed.IsSuccess)
        {
            return changed.Error!;
        }

        outbox.Enroll(db);
        if (status != QuoteStatus.Draft)
        {
            var verb = status switch { QuoteStatus.Sent => "gönderildi", QuoteStatus.Accepted => "kabul edildi", _ => "reddedildi" };
            await outbox.PublishAsync(new RecordTimelineEntry([quote.ContactId], ActivityKind.System, $"Teklif {verb}: {quote.Number}", SalesService.Money(quote.Total),
                clock.GetUtcNow(), SalesService.SourceModule, $"quote:{quote.Id}"));
        }

        await outbox.SaveChangesAndFlushMessagesAsync(cancellationToken);

        if (quote.OpportunityId is { } opportunityId && status is QuoteStatus.Accepted or QuoteStatus.Rejected)
        {
            var kind = status == QuoteStatus.Accepted ? StageKind.Won : StageKind.Lost;
            var pipelineId = await db.Opportunities.Where(o => o.Id == opportunityId).Select(o => o.PipelineId).FirstAsync(cancellationToken);
            var stage = await db.Stages.Where(s => s.PipelineId == pipelineId && s.Kind == kind).OrderBy(s => s.Position).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(cancellationToken);
            if (stage is null)
            {
                return id;
            }

            await sales.MoveAsync(opportunityId, stage.Value, lostReason: status == QuoteStatus.Rejected ? $"{quote.Number} numaralı teklif reddedildi" : null, cancellationToken: cancellationToken);
        }

        return id;
    }

    public async Task<Result<Guid>> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return QuoteErrors.NotFound;
        }

        if (quote.Status != QuoteStatus.Draft)
        {
            return QuoteErrors.OnlyDraftDeletable;
        }

        db.Quotes.Remove(quote);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<QuoteSettings> SettingsAsync(CancellationToken cancellationToken = default) =>
        await db.QuoteSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new QuoteSettings();

    public async Task<Result<bool>> SaveSettingsAsync(QuoteSettings input, CancellationToken cancellationToken = default)
    {
        if (input.ValidityDays is < 1 or > 365)
        {
            return new Error("quotes.validity", "Geçerlilik 1-365 gün olmalı.");
        }

        var settings = await db.QuoteSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new QuoteSettings();
            db.QuoteSettings.Add(settings);
        }

        settings.OrganizationName = input.OrganizationName?.Trim() ?? "";
        settings.Address = string.IsNullOrWhiteSpace(input.Address) ? null : input.Address.Trim();
        settings.LegalInfo = string.IsNullOrWhiteSpace(input.LegalInfo) ? null : input.LegalInfo.Trim();
        settings.DefaultTerms = string.IsNullOrWhiteSpace(input.DefaultTerms) ? null : input.DefaultTerms.Trim();
        settings.ValidityDays = input.ValidityDays;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<IReadOnlyList<QuoteView>> ToViewsAsync(IReadOnlyList<Quote> quotes, CancellationToken cancellationToken)
    {
        var names = await contacts.FindManyAsync([.. quotes.Select(q => q.ContactId).Distinct()], cancellationToken);
        var opportunityIds = quotes.Where(q => q.OpportunityId is not null).Select(q => q.OpportunityId!.Value).Distinct().ToList();
        var titles = await db.Opportunities.AsNoTracking().Where(o => opportunityIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Title, cancellationToken);
        return [.. quotes.Select(q => new QuoteView(q.Id, q.Number, q.ContactId, names.TryGetValue(q.ContactId, out var c) ? c.DisplayName : "(silinmiş kişi)",
            q.OpportunityId, q.OpportunityId is { } o ? titles.GetValueOrDefault(o) : null, q.IssuedOn, q.ValidUntil, q.Status, q.Terms,
            [.. q.Lines.OrderBy(l => l.Position).Select(l => new QuoteLineView(l.Position, l.Description, l.Quantity, l.Unit, l.UnitPrice, l.DiscountRate, l.VatRate, l.Net, l.Vat))],
            q.Subtotal, q.VatTotal, q.Total))];
    }
}
