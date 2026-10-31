using System.IO.Compression;
using System.Text;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Application.Banking;
using Arma.Modules.Giving.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class BankTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Csv_statement_with_turkish_headers_and_debit_credit_columns_is_read()
    {
        var csv = """
            Hesap Hareketleri;;;;
            İşlem Tarihi;Açıklama;Borç;Alacak;Bakiye
            03.01.2026;FAST GELEN - AYŞE DEMİR - TR330006100519786457841326 - Bağış;;1.250,50;5.000,00
            04.01.2026;Kira ödemesi;2.000,00;;3.000,00
            """;
        var parsed = StatementParser.Parse("ekstre.csv", Encoding.UTF8.GetBytes(csv)).Value!;
        parsed.Format.ShouldBe("csv");
        parsed.Rows.Count.ShouldBe(2);
        parsed.Rows[0].ShouldBe(new StatementRow(new DateOnly(2026, 1, 3), 1250.50m,
            "FAST GELEN - AYŞE DEMİR - TR330006100519786457841326 - Bağış", null, "TR330006100519786457841326", null));
        parsed.Rows[1].Amount.ShouldBe(-2000m);
    }

    [Fact]
    public void Windows_1254_csv_xlsx_and_mt940_statements_are_read()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var legacy = Encoding.GetEncoding(1254).GetBytes("Tarih,Tutar,Açıklama\n05/02/2026,\"300,00\",Gönderen: Şükrü Öz\n");
        var csv = StatementParser.Parse("eski.csv", legacy).Value!;
        csv.Rows.Single().Description.ShouldBe("Gönderen: Şükrü Öz");
        csv.Rows.Single().Amount.ShouldBe(300m);

        var xlsx = StatementParser.Parse("ekstre.xlsx", Xlsx([["Tarih", "Tutar", "Açıklama", "Dekont No"], ["46027", "750", "EFT MEHMET KAYA", "A-1"], ["46028", "-50", "Masraf", "A-2"]])).Value!;
        xlsx.Format.ShouldBe("xlsx");
        xlsx.Rows.Count.ShouldBe(2);
        xlsx.Rows[0].ShouldBe(new StatementRow(new DateOnly(2026, 1, 5), 750m, "EFT MEHMET KAYA", null, null, "A-1"));

        var mt940 = """
            :20:STMT
            :25:TR330006100519786457841326
            :60F:C260105TRY1000,00
            :61:2601060106C500,00NTRFNONREF//B123
            :86:HAVALE ALI VELI BAGIS
            :61:2601070107D100,00NTRFNONREF//B124
            :86:ODEME
            :62F:C260107TRY1400,00
            """;
        var parsed = StatementParser.Parse("hesap.sta", Encoding.ASCII.GetBytes(mt940)).Value!;
        parsed.Rows.Count.ShouldBe(2);
        parsed.Rows[0].ShouldBe(new StatementRow(new DateOnly(2026, 1, 6), 500m, "HAVALE ALI VELI BAGIS", null, null, "B123"));
        parsed.Rows[1].Amount.ShouldBe(-100m);

        StatementParser.Parse("bos.csv", Encoding.UTF8.GetBytes("a;b\n1;2")).Error!.Code.ShouldBe("bank.no_header");
    }

    [Fact]
    public async Task Import_suggests_by_name_then_iban_and_confirming_records_a_bank_transfer_once()
    {
        RequiresDatabase.Check();
        var suffix = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var last = "Havaleci" + new string(suffix.Where(char.IsLetter).DefaultIfEmpty('A').ToArray());
        var donor = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Zeynep", last, null, null, null), ct).Value());
        var fund = await Scoped<FundService, Guid>(s => s.CreateAsync(new SaveFundRequest("Su kuyusu öğrenci " + suffix, null, true, false), ct).Value());
        const string iban = "TR330006100519786457841326";
        var csv = $"""
            Tarih;Açıklama;Tutar
            10.03.2026;FAST - ZEYNEP {last.ToUpperInvariant()} - {iban} - SU KUYUSU OGRENCI {suffix};1.000,00
            11.03.2026;EFT - BILINMEYEN KISI;250,00
            12.03.2026;Masraf;-15,00
            """;

        var first = await Scoped<BankService, ImportSummary>(s => s.ImportAsync("mart.csv", Encoding.UTF8.GetBytes(csv), ct).Value());
        (first.Credits, first.New, first.Duplicates, first.Suggested).ShouldBe((2, 2, 0, 1));
        var again = await Scoped<BankService, ImportSummary>(s => s.ImportAsync("mart-tekrar.csv", Encoding.UTF8.GetBytes(csv), ct).Value());
        (again.New, again.Duplicates).ShouldBe((0, 2));

        var open = await Scoped<BankService, IReadOnlyList<BankLineView>>(s => s.LinesAsync(BankLineStatus.Open, first.StatementId, ct));
        var line = open.Single(l => l.SuggestedContactId is not null);
        (line.SuggestedContactId, line.SuggestedFundId, line.SuggestionReason, line.Iban).ShouldBe((donor, fund, "Ad", iban));

        (await Scoped<BankService, int>(s => s.ConfirmSuggestedAsync(first.StatementId, ct))).ShouldBe(1);
        var matched = (await Scoped<BankService, IReadOnlyList<BankLineView>>(s => s.LinesAsync(BankLineStatus.Matched, first.StatementId, ct))).Single();
        var donation = (await Scoped<DonationService, DonationView?>(s => s.GetAsync(matched.DonationId!.Value, ct)))!;
        (donation.ContactId, donation.Amount, donation.ReceivedOn, donation.FundId, donation.Method).ShouldBe((donor, 1000m, new DateOnly(2026, 3, 10), (Guid?)fund, PaymentMethod.BankTransfer));
        (await factory.WithScopeAsync(sp => sp.GetRequiredService<BankService>().ConfirmAsync(matched.Id, donor, null, ct))).Error!.Code.ShouldBe("bank.not_open");

        var unknown = open.Single(l => l.SuggestedContactId is null).Id;
        await Scoped<BankService, Guid>(s => s.IgnoreAsync(unknown, ct).Value());
        await Scoped<BankService, Guid>(s => s.ReopenAsync(unknown, ct).Value());

        // Aynı IBAN'dan gelen sonraki havale, açıklamada ad olmasa da aynı bağışçıya önerilir.
        var april = $"Tarih;Açıklama;Tutar\n05.04.2026;GELEN EFT {iban};500,00\n";
        var next = await Scoped<BankService, ImportSummary>(s => s.ImportAsync("nisan.csv", Encoding.UTF8.GetBytes(april), ct).Value());
        var ibanLine = (await Scoped<BankService, IReadOnlyList<BankLineView>>(s => s.LinesAsync(BankLineStatus.Open, next.StatementId, ct))).Single();
        (ibanLine.SuggestedContactId, ibanLine.SuggestionReason).ShouldBe((donor, "IBAN"));
    }

    [Fact]
    public async Task Bulk_receipts_number_pending_donations_in_date_order_and_annual_statement_lists_the_year()
    {
        RequiresDatabase.Check();
        var donor = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Yıllık", "Bağışçı", null, null, null), ct).Value());
        const int year = 2019;
        var late = await Donate(donor, 200m, new DateOnly(year, 12, 20));
        var early = await Donate(donor, 100m, new DateOnly(year, 2, 1));
        await Donate(donor, 999m, new DateOnly(year + 1, 1, 2));

        (await Scoped<DonationService, int>(s => s.PendingReceiptCountAsync(year, ct))).ShouldBe(2);
        (await Scoped<DonationService, int>(s => s.IssuePendingReceiptsAsync(year, ct))).ShouldBe(2);
        (await Scoped<DonationService, int>(s => s.PendingReceiptCountAsync(year, ct))).ShouldBe(0);
        var earlyNumber = (await Scoped<DonationService, DonationView?>(s => s.GetAsync(early, ct)))!.ReceiptNumber!;
        var lateNumber = (await Scoped<DonationService, DonationView?>(s => s.GetAsync(late, ct)))!.ReceiptNumber!;
        string.CompareOrdinal(earlyNumber, lateNumber).ShouldBeLessThan(0);

        var statement = await Scoped<DonationService, AnnualStatement>(s => s.AnnualStatementAsync(donor, year, ct));
        statement.Donations.Select(d => d.Id).ShouldBe([early, late]);
        statement.Totals.Single().Amount.ShouldBe(300m);
    }

    private Task<Guid> Donate(Guid contact, decimal amount, DateOnly on) =>
        Scoped<DonationService, Guid>(s => s.RecordAsync(new SaveDonationRequest(contact, amount, null, on, null, PaymentMethod.Cash, null, null, null), ct).Value());

    private static byte[] Xlsx(string[][] rows)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            var sheet = new StringBuilder("""<?xml version="1.0" encoding="UTF-8"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
            for (var r = 0; r < rows.Length; r++)
            {
                sheet.Append($"<row r=\"{r + 1}\">");
                for (var c = 0; c < rows[r].Length; c++)
                {
                    var cell = $"{(char)('A' + c)}{r + 1}";
                    var value = rows[r][c];
                    sheet.Append(decimal.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out _)
                        ? $"<c r=\"{cell}\"><v>{value}</v></c>"
                        : $"<c r=\"{cell}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value)}</t></is></c>");
                }

                sheet.Append("</row>");
            }

            sheet.Append("</sheetData></worksheet>");
            using var writer = new StreamWriter(zip.CreateEntry("xl/worksheets/sheet1.xml").Open());
            writer.Write(sheet.ToString());
        }

        return buffer.ToArray();
    }

    private Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull =>
        factory.WithScopeAsync(sp => action(sp.GetRequiredService<TService>()));
}
