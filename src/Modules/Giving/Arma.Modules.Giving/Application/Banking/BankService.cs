using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Giving.Application.Banking;

public sealed record ImportSummary(Guid StatementId, int Credits, int New, int Duplicates, int Suggested);

public sealed record StatementView(Guid Id, string FileName, string Format, DateTimeOffset ImportedAt, int CreditCount, int DuplicateCount, int OpenCount);

public sealed record BankLineView(
    Guid Id, DateOnly BookedOn, decimal Amount, string? Description, string? Counterparty, string? Iban, string? BankReference, BankLineStatus Status,
    Guid? SuggestedContactId, string? SuggestedContactName, Guid? SuggestedFundId, string? SuggestionReason, Guid? ContactId, string? ContactName, Guid? DonationId);

/// <summary>Havale/EFT ekstresini içe aktarır, bağışçı önerir ve onaylanan hareketleri bağışa çevirir.</summary>
public sealed class BankService(GivingDbContext db, DonationService donations, IContactDirectory contacts)
{
    public const int MaxFileBytes = 10 * 1024 * 1024;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly char[] Separators = ['-', '/', '|', ',', ';', ':', '*', '(', ')', '\n'];

    public async Task<Result<ImportSummary>> ImportAsync(string fileName, byte[] content, CancellationToken cancellationToken = default)
    {
        if (content.Length == 0 || content.Length > MaxFileBytes)
        {
            return new Error("bank.size", "Dosya boş veya 10 MB'tan büyük.");
        }

        var parsed = StatementParser.Parse(fileName, content);
        if (!parsed.IsSuccess)
        {
            return parsed.Error!;
        }

        var credits = parsed.Value!.Rows.Where(r => r.Amount > 0).ToList();
        if (credits.Count == 0)
        {
            return new Error("bank.no_credits", "Dosyada hesaba gelen (alacak) hareket yok.");
        }

        var keys = Keys(credits);
        var existing = (await db.BankLines.Where(l => keys.Contains(l.Key)).Select(l => l.Key).ToListAsync(cancellationToken)).ToHashSet();
        var statement = BankStatement.Create(Path.GetFileName(fileName), parsed.Value.Format, credits.Count, existing.Count);
        db.BankStatements.Add(statement);
        var added = new List<BankLine>();
        for (var i = 0; i < credits.Count; i++)
        {
            if (!existing.Contains(keys[i]))
            {
                added.Add(BankLine.Create(statement.Id, keys[i], credits[i]));
            }
        }

        db.BankLines.AddRange(added);
        var suggested = await SuggestAsync(added, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return new ImportSummary(statement.Id, credits.Count, added.Count, existing.Count, suggested);
    }

    public async Task<IReadOnlyList<StatementView>> StatementsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.BankStatements.AsNoTracking().OrderByDescending(s => s.CreatedAt).Take(50)
            .Select(s => new { s.Id, s.FileName, s.Format, s.CreatedAt, s.CreditCount, s.DuplicateCount, Open = db.BankLines.Count(l => l.StatementId == s.Id && l.Status == BankLineStatus.Open) })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(s => new StatementView(s.Id, s.FileName, s.Format, s.CreatedAt, s.CreditCount, s.DuplicateCount, s.Open))];
    }

    public async Task<IReadOnlyList<BankLineView>> LinesAsync(BankLineStatus? status, Guid? statementId = null, CancellationToken cancellationToken = default)
    {
        var query = db.BankLines.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(l => l.Status == s);
        }

        if (statementId is { } id)
        {
            query = query.Where(l => l.StatementId == id);
        }

        var lines = await query.OrderByDescending(l => l.BookedOn).ThenBy(l => l.CreatedAt).Take(500).ToListAsync(cancellationToken);
        var ids = lines.SelectMany(l => new[] { l.SuggestedContactId, l.ContactId }).OfType<Guid>().Distinct().ToList();
        var names = ids.Count == 0 ? new Dictionary<Guid, ContactSummary>() : await contacts.FindManyAsync(ids, cancellationToken);
        return [.. lines.Select(l => new BankLineView(
            l.Id, l.BookedOn, l.Amount, l.Description, l.Counterparty, l.Iban, l.BankReference, l.Status,
            l.SuggestedContactId, l.SuggestedContactId is { } sc && names.TryGetValue(sc, out var a) ? a.DisplayName : null, l.SuggestedFundId, l.SuggestionReason,
            l.ContactId, l.ContactId is { } c && names.TryGetValue(c, out var b) ? b.DisplayName : null, l.DonationId))];
    }

    /// <summary>Hareketi seçilen kişinin havale bağışı olarak kaydeder. Bağış ve hareketin durumu birlikte kaydedilir.</summary>
    public async Task<Result<Guid>> ConfirmAsync(Guid lineId, Guid contactId, Guid? fundId, CancellationToken cancellationToken = default)
    {
        var line = await db.BankLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return new Error("bank.not_found", "Hareket bulunamadı.");
        }

        if (line.Status != BankLineStatus.Open)
        {
            return new Error("bank.not_open", "Bu hareket zaten işlenmiş.");
        }

        var note = line.Description is null ? "Havale/EFT" : $"Havale/EFT: {line.Description}";
        // Bağışçının açık taahhüdü varsa havale onun taksidi sayılır.
        var pledge = await donations.OpenPledgeForAsync(contactId, Donation.DefaultCurrency, cancellationToken);
        fundId ??= pledge?.FundId;
        var request = new SaveDonationRequest(contactId, line.Amount, Donation.DefaultCurrency, line.BookedOn, fundId, PaymentMethod.BankTransfer, line.BankReference, note.Length > 1000 ? note[..1000] : note, null, pledge?.Id);
        return await donations.RecordAsync(request, cancellationToken, donation => line.Match(contactId, donation.Id));
    }

    /// <summary>Önerisi olan tüm açık hareketleri önerilen kişi ve fonla onaylar. Onaylanan sayıyı döner.</summary>
    public async Task<int> ConfirmSuggestedAsync(Guid? statementId = null, CancellationToken cancellationToken = default)
    {
        var query = db.BankLines.AsNoTracking().Where(l => l.Status == BankLineStatus.Open && l.SuggestedContactId != null);
        if (statementId is { } id)
        {
            query = query.Where(l => l.StatementId == id);
        }

        var lines = await query.Select(l => new { l.Id, Contact = l.SuggestedContactId!.Value, l.SuggestedFundId }).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var line in lines)
        {
            if ((await ConfirmAsync(line.Id, line.Contact, line.SuggestedFundId, cancellationToken)).IsSuccess)
            {
                count++;
            }
        }

        return count;
    }

    public Task<Result<Guid>> IgnoreAsync(Guid lineId, CancellationToken cancellationToken = default) => ChangeAsync(lineId, l => l.Ignore(), cancellationToken);

    public Task<Result<Guid>> ReopenAsync(Guid lineId, CancellationToken cancellationToken = default) => ChangeAsync(lineId, l => l.Reopen(), cancellationToken);

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        var lines = await db.BankLines.Where(l => l.ContactId == from || l.SuggestedContactId == from).ToListAsync(cancellationToken);
        lines.ForEach(l => l.ChangeContact(from, to));
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Önce aynı IBAN'dan daha önce onaylanmış harekete bakar; yoksa gönderen adını ve açıklamadaki ad parçalarını
    /// kişi aramasında tam eşleşme olarak arar. Açıklamada fon adı geçiyorsa fonu da önerir.
    /// </summary>
    private async Task<int> SuggestAsync(IReadOnlyList<BankLine> lines, CancellationToken cancellationToken)
    {
        var ibans = lines.Select(l => l.Iban).OfType<string>().Distinct().ToList();
        var known = ibans.Count == 0
            ? new Dictionary<string, Guid>()
            : (await db.BankLines.AsNoTracking().Where(l => l.Status == BankLineStatus.Matched && l.Iban != null && ibans.Contains(l.Iban))
                .OrderByDescending(l => l.UpdatedAt).Select(l => new { Iban = l.Iban!, Contact = l.ContactId!.Value }).ToListAsync(cancellationToken))
                .GroupBy(x => x.Iban).ToDictionary(g => g.Key, g => g.First().Contact);
        var funds = await db.Funds.AsNoTracking().Where(f => f.IsActive).Select(f => new { f.Id, f.Name }).ToListAsync(cancellationToken);
        var count = 0;
        foreach (var line in lines)
        {
            var text = Fold($"{line.Counterparty} {line.Description}");
            var fund = funds.FirstOrDefault(f => text.Contains(Fold(f.Name), StringComparison.Ordinal))?.Id;
            if (line.Iban is { } iban && known.TryGetValue(iban, out var byIban))
            {
                line.Suggest(byIban, fund, "IBAN");
            }
            else if (await ByNameAsync(line, cancellationToken) is { } byName)
            {
                line.Suggest(byName, fund, "Ad");
            }
            else
            {
                line.Suggest(null, fund, null);
            }

            count += line.SuggestedContactId is null ? 0 : 1;
        }

        return count;
    }

    private async Task<Guid?> ByNameAsync(BankLine line, CancellationToken cancellationToken)
    {
        var candidates = new[] { line.Counterparty }
            .Concat((line.Description ?? "").Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .OfType<string>()
            .Select(s => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(s => s.Split(' ').Length is >= 2 and <= 4 && s.All(c => char.IsLetter(c) || c is ' ' or '.' or '\''))
            .Distinct()
            .Take(6);
        foreach (var name in candidates)
        {
            var found = (await contacts.SearchAsync(name, 3, cancellationToken))
                .Where(c => Fold(c.DisplayName) == Fold(name))
                .ToList();
            if (found.Count == 1)
            {
                return found[0].Id;
            }
        }

        return null;
    }

    /// <summary>Banka metni çoğunlukla büyük harfli ve Türkçe karaktersizdir; "ŞÜKRÜ ÖZ" ile "SUKRU OZ" eşit sayılır.</summary>
    private static string Fold(string text) =>
        new([.. text.Trim().ToLower(Turkish).Select(c => c switch
        {
            'ı' => 'i', 'ş' => 's', 'ğ' => 'g', 'ü' => 'u', 'ö' => 'o', 'ç' => 'c', 'â' => 'a', 'î' => 'i', 'û' => 'u', _ => c,
        })]);

    private async Task<Result<Guid>> ChangeAsync(Guid lineId, Func<BankLine, Result<bool>> change, CancellationToken cancellationToken)
    {
        var line = await db.BankLines.FirstOrDefaultAsync(l => l.Id == lineId, cancellationToken);
        if (line is null)
        {
            return new Error("bank.not_found", "Hareket bulunamadı.");
        }

        var result = change(line);
        if (!result.IsSuccess)
        {
            return result.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return lineId;
    }

    /// <summary>Tarih, tutar, açıklama ve referanstan parmak izi; aynı dosyada birebir aynı iki hareket sıra numarasıyla ayrılır.</summary>
    private static List<string> Keys(IReadOnlyList<StatementRow> rows)
    {
        var seen = new Dictionary<string, int>();
        var keys = new List<string>();
        foreach (var row in rows)
        {
            var raw = string.Join('|', row.BookedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture), row.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                (row.Description ?? "").Trim().ToLowerInvariant(), (row.Reference ?? "").Trim(), row.Iban ?? "");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))[..32];
            var n = seen[hash] = seen.GetValueOrDefault(hash) + 1;
            keys.Add($"{hash}-{n}");
        }

        return keys;
    }
}
