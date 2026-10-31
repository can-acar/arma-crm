using Arma.Infrastructure.Messaging;
using System.Security.Cryptography;
using Arma.Modules.Comms.Contracts;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Modules;
using Arma.SharedKernel.Payments;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Giving.Application;

public sealed record RecurringDonationView(
    Guid Id,
    Guid ContactId,
    string ContactName,
    decimal Amount,
    string Currency,
    string? FundName,
    string Provider,
    RecurringStatus Status,
    int DayOfMonth,
    DateOnly? NextChargeOn,
    DateOnly? LastChargedOn,
    string? CardLabel,
    int FailureCount,
    string? LastError,
    int PaymentCount,
    decimal PaidTotal,
    DateTimeOffset CreatedAt);

public sealed record RecurringRunResult(int Charged, int Failed, int Paused);

/// <summary>Kayıtlı karttan aylık tahsil edilen düzenli bağışlar.</summary>
public sealed class RecurringDonationService(
    GivingDbContext db,
    DonationService donations,
    IContactDirectory contacts,
    IPaymentGateway payments,
    IDataProtectionProvider protection,
    IDbContextOutbox outbox,
    TimeProvider clock,
    ILogger<RecurringDonationService> logger)
{
    /// <summary>Kayıtlı karttan sunucunun yaptığı tahsilatta ödeyen IP'si olarak gönderilir.</summary>
    internal const string ServerIp = "127.0.0.1";

    public async Task<IReadOnlyList<RecurringDonationView>> ListAsync(RecurringStatus? status = null, Guid? contactId = null, CancellationToken cancellationToken = default)
    {
        var query = db.RecurringDonations.AsNoTracking().Where(r => r.Status != RecurringStatus.PendingSetup);
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        if (contactId is { } c)
        {
            query = query.Where(r => r.ContactId == c);
        }

        var rows = await query.OrderByDescending(r => r.CreatedAt)
            .Select(r => new
            {
                Recurring = r,
                Fund = db.Funds.Where(f => f.Id == r.FundId).Select(f => f.Name).FirstOrDefault(),
                Count = db.Donations.Count(d => d.RecurringDonationId == r.Id && d.Status == DonationStatus.Completed),
                Total = db.Donations.Where(d => d.RecurringDonationId == r.Id && d.Status == DonationStatus.Completed).Sum(d => (decimal?)d.Amount) ?? 0m,
            })
            .ToListAsync(cancellationToken);
        var names = await contacts.FindManyAsync([.. rows.Select(r => r.Recurring.ContactId).Distinct()], cancellationToken);
        return [.. rows.Select(r => new RecurringDonationView(r.Recurring.Id, r.Recurring.ContactId,
            names.TryGetValue(r.Recurring.ContactId, out var contact) ? contact.DisplayName : "(silinmiş kişi)",
            r.Recurring.Amount, r.Recurring.Currency, r.Fund, r.Recurring.Provider, r.Recurring.Status, r.Recurring.DayOfMonth,
            r.Recurring.NextChargeOn, r.Recurring.LastChargedOn, r.Recurring.CardLabel, r.Recurring.FailureCount, r.Recurring.LastError,
            r.Count, r.Total, r.Recurring.CreatedAt))];
    }

    /// <summary>Düzenli bağışı durdurur ve kart jetonunu siler. <paramref name="contactId"/> verilirse yalnızca o kişinin kaydı.</summary>
    public async Task<Result<Guid>> CancelAsync(Guid id, Guid? contactId = null, CancellationToken cancellationToken = default)
    {
        var recurring = await db.RecurringDonations.FirstOrDefaultAsync(r => r.Id == id && (contactId == null || r.ContactId == contactId), cancellationToken);
        if (recurring is null)
        {
            return RecurringErrors.NotFound;
        }

        recurring.Cancel(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> ResumeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var recurring = await db.RecurringDonations.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (recurring is null)
        {
            return RecurringErrors.NotFound;
        }

        var result = recurring.Resume(Today);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>Günü gelen düzenli bağışları kayıtlı karttan tahsil eder. Her tahsilat ayrı bağış kaydı olur.</summary>
    public async Task<RecurringRunResult> ChargeDueAsync(CancellationToken cancellationToken = default)
    {
        var today = Today;
        var due = await db.RecurringDonations.AsNoTracking()
            .Where(r => r.Status == RecurringStatus.Active && r.NextChargeOn != null && r.NextChargeOn <= today)
            .OrderBy(r => r.NextChargeOn).Select(r => r.Id).Take(500).ToListAsync(cancellationToken);

        int charged = 0, failed = 0, paused = 0;
        foreach (var id in due)
        {
            db.ChangeTracker.Clear();
            switch (await ChargeOneAsync(id, today, cancellationToken))
            {
                case true:
                    charged++;
                    break;
                case false:
                    failed++;
                    break;
                case null:
                    paused++;
                    break;
            }
        }

        if (due.Count > 0)
        {
            logger.LogInformation("Düzenli bağış: {Charged} tahsil edildi, {Failed} başarısız, {Paused} durduruldu", charged, failed, paused);
        }

        return new RecurringRunResult(charged, failed, paused);
    }

    /// <returns>true: tahsil edildi, false: başarısız (yeniden denenecek), null: durduruldu.</returns>
    private async Task<bool?> ChargeOneAsync(Guid id, DateOnly today, CancellationToken cancellationToken)
    {
        var recurring = await db.RecurringDonations.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (recurring is null || !recurring.IsDue(today))
        {
            return false;
        }

        var contact = await contacts.FindAsync(recurring.ContactId, cancellationToken);
        var token = Unprotect(recurring.CardToken);
        var donation = Donation.StartOnline(recurring.ContactId, recurring.Amount, recurring.FundId, today, recurring.Provider).Value!;
        donation.AssignRecurring(recurring.Id);
        db.Donations.Add(donation);
        await db.SaveChangesAsync(cancellationToken);

        var result = contact?.Email is not { Length: > 0 } email ? PaymentResult.Failed("Bağışçının e-posta adresi yok.")
            : token is null ? PaymentResult.Failed("Kayıtlı kart jetonu okunamadı.")
            : await payments.ChargeStoredCardAsync(new StoredCardCharge(recurring.Provider, donation.Id.ToString("N"), recurring.Amount, recurring.Currency,
                "Düzenli bağış", Payer(contact, email), new StoredCard(token, recurring.CardLabel)), cancellationToken);

        if (result.Success)
        {
            donation.MarkPaid(result.ProviderPaymentId!, recurring.Amount);
            recurring.RecordCharge(today);
            await donations.SaveWithTimelineAsync(donation, cancellationToken, await EmailAsync(recurring.ContactId, "Düzenli bağışınız alındı",
                $"Sayın {{{{adsoyad}}}},\n\nBu ayki {Money.Format(recurring.Amount, recurring.Currency)} tutarındaki düzenli bağışınız kayıtlı kartınızdan alındı. Desteğiniz için teşekkür ederiz.", cancellationToken));
            return true;
        }

        donation.MarkFailed(result.Error);
        var stopped = recurring.RecordFailure(result.Error ?? "Tahsilat başarısız", today);
        outbox.Enroll(db);
        if (stopped)
        {
            await outbox.PublishAsync(await EmailAsync(recurring.ContactId, "Düzenli bağışınız durdu",
                $"Sayın {{{{adsoyad}}}},\n\nDüzenli bağışınız kayıtlı kartınızdan {RecurringDonation.MaxFailures} kez alınamadığı için durduruldu. Yeniden başlatmak için bizimle iletişime geçebilir ya da bağış formundan yeni bir düzenli bağış başlatabilirsiniz.", cancellationToken));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        logger.LogWarning("Düzenli bağış tahsil edilemedi ({RecurringId}, {Attempt}. deneme): {Error}", recurring.Id, recurring.FailureCount, result.Error);
        return stopped ? null : false;
    }

    private string? Unprotect(string? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return protection.CreateProtector(OnlineGivingService.CardProtectorPurpose).Unprotect(value);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static Payer Payer(ContactSummary contact, string email)
    {
        var parts = contact.DisplayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new Payer(parts.ElementAtOrDefault(0) ?? "-", parts.ElementAtOrDefault(1) ?? "-", email, contact.Phone, ServerIp);
    }

    private async Task<SendTransactionalEmail> EmailAsync(Guid contactId, string subject, string body, CancellationToken cancellationToken)
    {
        var organization = await db.ReceiptSettings.Select(r => r.OrganizationName).FirstOrDefaultAsync(cancellationToken);
        return new SendTransactionalEmail(contactId, subject, body + (string.IsNullOrWhiteSpace(organization) ? "" : $"\n\n{organization}"));
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
}

public static class RecurringErrors
{
    public static readonly Error NotFound = new("recurring.not_found", "Düzenli bağış bulunamadı.");
}

/// <summary>Günü gelen düzenli bağışları saatte bir tahsil eder.</summary>
internal sealed class RecurringDonationJob(RecurringDonationService service) : IScheduledJob
{
    public string Name => "Düzenli bağış tahsilatı";

    public TimeSpan Interval => TimeSpan.FromHours(1);

    public Task RunAsync(CancellationToken cancellationToken) => service.ChargeDueAsync(cancellationToken);
}
