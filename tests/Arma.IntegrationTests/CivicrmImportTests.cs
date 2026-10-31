using System.Text;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Membership.Application;
using Arma.Modules.Membership.Domain;
using Arma.SharedKernel.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class CivicrmImportTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Csv_rows_are_read_case_insensitively_and_located_headers_are_also_known_by_their_plain_name()
    {
        var rows = ImportFile.Read(Encoding.UTF8.GetBytes("Contact ID,email (Home),Total Amount\n7,a@b.org,\"1,250.00\"\n")).Value!;
        rows.Single().Line.ShouldBe(2);
        rows.Single().Get("EMAIL").ShouldBe("a@b.org");
        ImportParsing.TryAmount(rows.Single().Get("Total Amount"), out var amount).ShouldBeTrue();
        amount.ShouldBe(1250m);
        ImportParsing.TryAmount("1.250,50", out amount).ShouldBeTrue();
        amount.ShouldBe(1250.50m);
        ImportParsing.Date("2024-03-05 14:22:00").ShouldBe(new DateOnly(2024, 3, 5));
        ImportParsing.Date("05.03.2024").ShouldBe(new DateOnly(2024, 3, 5));

        var importer = new CivicrmContactImporter(null!, null!);
        ImportFile.MissingColumns(importer, rows).ShouldBe(["First Name veya Last Name veya Organization Name veya Household Name veya Display Name"]);
    }

    [Fact]
    public async Task Contacts_contributions_and_memberships_are_imported_once_and_linked_by_civicrm_id()
    {
        RequiresDatabase.Check();
        var b = Random.Shared.Next(1_000_000, 9_000_000);
        var tag = "Civi" + b;
        var existingEmail = $"mevcut{b}@example.org";
        var existing = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Mevcut", "Kişi", existingEmail, null, null), ct).Value());

        var contacts = $"""
            Contact ID,Contact Type,First Name,Last Name,Display Name,Organization Name,Household Name,Email,Phone,Birth Date,Street Address,City,State,Postal Code,Country,Tag(s),Group(s),Contact is in Trash
            {b},Individual,Ayşe,Demir,Ayşe Demir,,,ayse{b}@example.org,0532 111 22 33,1985-04-15 00:00:00,Atatürk Cad. No:5,Çankaya,Ankara,06420,Turkey,"{tag}, Bağışçı",Bülten {b},0
            {b + 1},Organization,,,Umut Vakfı {b},Umut Vakfı {b},,,,,,,İzmir,,Turkey,{tag},,0
            {b + 2},Individual,Mevcut,Kişi,Mevcut Kişi,,,{existingEmail},,,,,,,,,,0
            {b + 3},Individual,Silinmiş,Kişi,Silinmiş Kişi,,,,,,,,,,,,,1
            {b + 4},Individual,,,Tekisim,,,,,,,,,,,,,0
            {b + 5},Household,,,Yılmaz Ailesi {b},,Yılmaz Ailesi {b},,,,,,,,,,,0
            """;
        var first = await Import<CivicrmContactImporter>(contacts);
        (first.Created, first.Matched, first.Skipped).ShouldBe((3, 1, 1));
        first.Issues.Single().Line.ShouldBe(6);
        var again = await Import<CivicrmContactImporter>(contacts);
        (again.Created, again.Matched, again.Skipped).ShouldBe((0, 0, 5));

        var ids = await Scoped<ContactService, IReadOnlyDictionary<string, Guid>>(s =>
            s.FindByExternalKeysAsync([$"civicrm:{b}", $"civicrm:{b + 1}", $"civicrm:{b + 2}"], ct));
        ids[$"civicrm:{b + 2}"].ShouldBe(existing);
        var ayse = (await Scoped<ContactService, ContactDetail?>(s => s.GetAsync(ids[$"civicrm:{b}"], ct)))!;
        (ayse.BirthDate, ayse.Addresses.Single().City, ayse.Addresses.Single().District).ShouldBe((new DateOnly(1985, 4, 15), "Ankara", "Çankaya"));
        ayse.Tags.Select(t => t.Name).ShouldBe([tag, "Bağışçı"], ignoreOrder: true);
        ayse.Groups.Single().Name.ShouldBe($"Bülten {b}");

        var fund = "Burs fonu " + b;
        var contributions = $"""
            Contact ID,Display Name,Contribution ID,Financial Type,Total Amount,Currency,Date Received,Payment Method,Contribution Status,Transaction ID
            {b},Ayşe Demir,{b}1,{fund},"1,500.00",TRY,2025-12-20 10:00:00,EFT,Completed,TX-{b}
            {b},Ayşe Demir,{b}2,Donation,250.00,TRY,2026-01-05 09:00:00,Cash,Completed,
            {b},Ayşe Demir,{b}3,Donation,99.00,TRY,2026-01-06 09:00:00,Credit Card,Pending,
            {b},Ayşe Demir,{b}4,Member Dues,300.00,TRY,2026-01-07 09:00:00,Cash,Completed,
            {b + 1},Umut Vakfı,{b}5,{fund},10000,USD,2026-02-01,Check,Refunded,
            999999999,Bilinmeyen,{b}6,Donation,50,TRY,2026-02-02,Cash,Completed,
            """;
        var gifts = await Import<CivicrmContributionImporter>(contributions);
        (gifts.Created, gifts.Skipped).ShouldBe((3, 2));
        gifts.Issues.Single().Reason.ShouldContain("Kişi bulunamadı");
        (await Import<CivicrmContributionImporter>(contributions)).Created.ShouldBe(0);

        var list = (await Scoped<DonationService, DonationPage>(s => s.ListAsync(new DonationFilter(ContactId: ids[$"civicrm:{b}"]), ct))).Items;
        list.Count.ShouldBe(2);
        var eft = list.Single(d => d.Amount == 1500m);
        (eft.FundName, eft.Method, eft.Reference, eft.ReceivedOn).ShouldBe((fund, PaymentMethod.BankTransfer, $"TX-{b}", new DateOnly(2025, 12, 20)));
        list.Single(d => d.Amount == 250m).FundId.ShouldBeNull();
        var refund = (await Scoped<DonationService, DonationPage>(s => s.ListAsync(new DonationFilter(ContactId: ids[$"civicrm:{b + 1}"]), ct))).Items.Single();
        (refund.Status, refund.Currency).ShouldBe((DonationStatus.Refunded, "USD"));

        var type = "Asil üye " + b;
        var members = $"""
            Contact ID,Display Name,Membership ID,Membership Type,Member Since,Membership Start Date,Membership End Date,Membership Status
            {b},Ayşe Demir,{b}1,{type},2020-03-01,2026-01-01,2026-12-31,Current
            {b + 1},Umut Vakfı,{b}2,{type},2019-01-01,2019-01-01,2020-12-31,Cancelled
            {b + 2},Mevcut Kişi,{b}3,Onursal {b},2018-05-01,2018-05-01,,Current
            """;
        var joined = await Import<CivicrmMembershipImporter>(members);
        (joined.Created, joined.Issues.Count).ShouldBe((3, 0));
        (await Import<CivicrmMembershipImporter>(members)).Skipped.ShouldBe(3);

        var ayseMember = (await Scoped<MembershipService, IReadOnlyList<MemberView>>(s => s.ForContactAsync(ids[$"civicrm:{b}"], ct))).Single();
        (ayseMember.TypeName, ayseMember.JoinedOn, ayseMember.StartOn, ayseMember.EndOn).ShouldBe((type, new DateOnly(2020, 3, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)));
        var cancelled = (await Scoped<MembershipService, IReadOnlyList<MemberView>>(s => s.ForContactAsync(ids[$"civicrm:{b + 1}"], ct))).Single();
        (cancelled.Status, cancelled.CancelledOn).ShouldBe((MembershipStatus.Cancelled, new DateOnly(2020, 12, 31)));
        var lifetime = (await Scoped<MembershipService, IReadOnlyList<MemberView>>(s => s.ForContactAsync(existing, ct))).Single();
        (lifetime.EndOn, lifetime.Status).ShouldBe(((DateOnly?)null, MembershipStatus.Current));
    }

    private Task<ImportOutcome> Import<TImporter>(string csv) where TImporter : IDataImporter =>
        factory.WithScopeAsync(sp => sp.GetServices<IDataImporter>().OfType<TImporter>().Single()
            .ImportAsync(ImportFile.Read(Encoding.UTF8.GetBytes(csv)).Value!, ct));

    private Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull =>
        factory.WithScopeAsync(sp => action(sp.GetRequiredService<TService>()));
}
