using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Giving.Application;

/// <summary>
/// CiviCRM "Katkıları dışa aktar" (Export Contributions) dosyasını bağış olarak aktarır. Yalnızca tamamlanmış ve iade
/// edilmiş katkılar alınır; mali tür ("Financial Type") fon olur. Aidat katkıları üyelik geçmişi olduğu için atlanır.
/// </summary>
public sealed class CivicrmContributionImporter(GivingDbContext db, DonationService donations, IContactRegistry registry, IAuditLog audit) : IDataImporter
{
    private const string SourceKey = "civicrm";

    private const int BatchSize = 200;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Fon açılmayan genel mali türler.</summary>
    private static readonly HashSet<string> GeneralTypes = new(["Donation", "Bağış"], StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> DuesTypes = new(["Member Dues", "Aidat", "Üyelik aidatı"], StringComparer.OrdinalIgnoreCase);

    public string Source => SourceKey;

    public string Label => "Bağışlar";

    public string Hint => "Katkıları Ara → tümünü seç → Katkıları dışa aktar → \"Birincil alanlar\". Mali tür fon olarak açılır; \"Member Dues\" katkıları atlanır.";

    public int Order => 20;

    public string Permission => GivingPermissions.Write;

    public IReadOnlyList<string> RequiredColumns => ["Contact ID", "Total Amount|Amount", "Date Received|Receive Date|Contribution Date"];

    public async Task<ImportOutcome> ImportAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        var issues = new List<ImportIssue>();
        var contactIds = await registry.FindByExternalKeysAsync(
            [.. rows.Select(r => r.Get("Contact ID")).OfType<string>().Select(id => ExternalKeys.For(SourceKey, id)).Distinct()], cancellationToken);
        var keys = rows.Select(Key).ToList();
        var known = (await db.Donations.Where(d => d.ExternalKey != null && keys.Contains(d.ExternalKey))
            .Select(d => d.ExternalKey!).ToListAsync(cancellationToken)).ToHashSet();
        var funds = await FundsAsync(rows, cancellationToken);
        var fundNames = funds.ToDictionary(f => f.Value, f => f.Key);
        var batch = new List<Donation>();
        int created = 0, skipped = 0;

        foreach (var (row, key) in rows.Zip(keys))
        {
            var name = row.Get("Display Name", "Sort Name") ?? $"Contact ID {row.Get("Contact ID")}";
            var status = row.Get("Contribution Status", "Status") ?? "Completed";
            var refunded = status.Equals("Refunded", StringComparison.OrdinalIgnoreCase) || status.Equals("İade edildi", StringComparison.OrdinalIgnoreCase);
            var type = row.Get("Financial Type");
            if (!known.Add(key) || (!refunded && !status.Equals("Completed", StringComparison.OrdinalIgnoreCase) && !status.Equals("Tamamlandı", StringComparison.OrdinalIgnoreCase))
                || (type is not null && DuesTypes.Contains(type)))
            {
                skipped++;
                continue;
            }

            if (row.Get("Contact ID") is not { } civiId || !contactIds.TryGetValue(ExternalKeys.For(SourceKey, civiId), out var contactId))
            {
                issues.Add(new ImportIssue(row.Line, name, "Kişi bulunamadı; önce kişiler dosyasını aktarın."));
                continue;
            }

            if (!ImportParsing.TryAmount(row.Get("Total Amount", "Amount"), out var amount))
            {
                issues.Add(new ImportIssue(row.Line, name, "Tutar okunamadı."));
                continue;
            }

            if (ImportParsing.Date(row.Get("Date Received", "Receive Date", "Contribution Date")) is not { } receivedOn)
            {
                issues.Add(new ImportIssue(row.Line, name, "Tarih okunamadı."));
                continue;
            }

            var reference = row.Get("Transaction ID", "Check Number", "Invoice Number");
            var note = string.Join(" · ", new[]
            {
                "CiviCRM'den aktarıldı",
                row.Get("Contribution Source", "Source"),
                row.Get("Receipt Date") is { } receipt ? $"makbuz: {receipt[..Math.Min(10, receipt.Length)]}" : null,
            }.OfType<string>());
            var method = Method(row.Get("Payment Method", "Payment Instrument"));
            var record = Donation.Record(contactId, amount, row.Get("Currency"), receivedOn, type is not null && funds.TryGetValue(type, out var fundId) ? fundId : null, method,
                reference, note, method == PaymentMethod.InKind ? row.Get("Contribution Source", "Source") ?? "Ayni bağış" : null);
            if (!record.IsSuccess)
            {
                issues.Add(new ImportIssue(row.Line, name, record.Error!.Message));
                continue;
            }

            var donation = record.Value!;
            donation.LinkExternal(key);
            if (refunded)
            {
                donation.Refund(null);
            }

            batch.Add(donation);
            created++;
            if (batch.Count == BatchSize)
            {
                await donations.SaveImportedAsync(batch, fundNames, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
        {
            await donations.SaveImportedAsync(batch, fundNames, cancellationToken);
        }

        await audit.WriteAsync(AuditAction.Imported, nameof(Donation), null,
            $"CiviCRM: {created} bağış aktarıldı, {skipped} satır atlandı, {issues.Count} satır hatalı", cancellationToken);
        return new ImportOutcome(created, 0, skipped, issues);
    }

    /// <summary>Katkı kimliği yoksa satırın içeriğinden kararlı bir anahtar üretilir; aynı dosya iki kez yüklenince yine eşleşir.</summary>
    private static string Key(ImportRow row)
    {
        if (row.Get("Contribution ID", "Internal Contribution ID") is { } id)
        {
            return ExternalKeys.For(SourceKey, "contribution-" + id);
        }

        var content = string.Join("|", row.Values.OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase).Select(v => $"{v.Key}={v.Value}"));
        return ExternalKeys.For(SourceKey, "contribution-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..32]);
    }

    /// <summary>Dosyadaki mali türler için fon bulur, yoksa (yayına kapalı) açar.</summary>
    private async Task<Dictionary<string, Guid>> FundsAsync(IReadOnlyList<ImportRow> rows, CancellationToken cancellationToken)
    {
        var comparer = StringComparer.Create(Turkish, ignoreCase: true);
        var funds = (await db.Funds.Select(f => new { f.Id, f.Name }).ToListAsync(cancellationToken))
            .GroupBy(f => f.Name, comparer).ToDictionary(g => g.Key, g => g.First().Id, comparer);
        var added = false;
        foreach (var type in rows.Select(r => r.Get("Financial Type")).OfType<string>().Distinct(comparer))
        {
            if (GeneralTypes.Contains(type) || DuesTypes.Contains(type) || funds.ContainsKey(type))
            {
                continue;
            }

            if (Fund.Create(type, "CiviCRM'den aktarıldı", isPublic: false) is { IsSuccess: true } fund)
            {
                db.Funds.Add(fund.Value!);
                funds[type] = fund.Value!.Id;
                added = true;
            }
        }

        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return funds;
    }

    private static PaymentMethod Method(string? value) => value?.Trim().ToLower(Turkish) switch
    {
        "cash" or "nakit" => PaymentMethod.Cash,
        "check" or "cheque" or "çek" => PaymentMethod.Check,
        "credit card" or "debit card" or "kredi kartı" or "banka kartı" => PaymentMethod.CreditCard,
        "eft" or "bank transfer" or "havale" or "havale/eft" or "direct debit" => PaymentMethod.BankTransfer,
        "in kind" or "ayni" => PaymentMethod.InKind,
        _ => PaymentMethod.Other,
    };
}
