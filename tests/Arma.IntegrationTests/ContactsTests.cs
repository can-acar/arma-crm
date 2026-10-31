using System.Text;
using Arma.Modules.Audit;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;
using Arma.SharedKernel.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class ContactsTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    // Algoritmaya uygun örnek TCKN'ler.
    private const string Tckn1 = "10000000146";
    private const string Tckn2 = "11111111110";

    [Fact]
    public async Task Changes_are_audited_and_sensitive_values_are_masked()
    {
        RequiresDatabase.Check();
        var id = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Ayşe", "Işık", "ayse@example.org", null, Tckn1), ct).Value());
        await Scoped<ContactService, Guid>(s => s.UpdateAsync(id, new UpdateContactRequest("Ayşe", "Işık", null, "ayse.isik@example.org", "0532 111 22 33", Tckn1, new DateOnly(1985, 4, 15), null,
            [new Address { City = "İzmir", District = "Konak", Line = "Alsancak" }]), ct).Value());

        var history = await Scoped<IAuditLog, IReadOnlyList<AuditRecord>>(a => a.ForEntityAsync(nameof(Contact), id.ToString(), ct));

        history.Select(h => h.Action).ShouldBe([AuditAction.Updated, AuditAction.Created]);
        var created = history[1];
        created.Changes.Single(c => c.Property == "NationalId").NewValue.ShouldBe("***");
        created.Changes.ShouldNotContain(c => c.Property == "SearchText");
        var updated = history[0];
        updated.Changes.Single(c => c.Property == "Email").ShouldSatisfyAllConditions(
            c => c.OldValue.ShouldBe("ayse@example.org"),
            c => c.NewValue.ShouldBe("ayse.isik@example.org"));
        updated.Changes.ShouldContain(c => c.Property == "Addresses");
        updated.Changes.ShouldNotContain(c => c.Property == "NationalId");
    }

    [Fact]
    public async Task Audit_log_cannot_be_modified()
    {
        RequiresDatabase.Check();
        await Scoped<ContactService, Guid>(s => s.CreateOrganizationAsync(new CreateOrganizationRequest("Denetim Vakfı", null, null, null), ct).Value());

        var ex = await Should.ThrowAsync<Exception>(() => factory.WithScopeAsync(async sp =>
            await sp.GetRequiredService<AuditDbContext>().Database.ExecuteSqlRawAsync("DELETE FROM audit.audit_log", ct)));
        ex.Message.ShouldContain("değiştirilemez");
    }

    [Fact]
    public async Task Same_national_id_cannot_be_registered_twice()
    {
        RequiresDatabase.Check();
        await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Ali", "Veli", null, null, Tckn2), ct).Value());
        var second = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactService>().CreatePersonAsync(new CreatePersonRequest("Ali", "Başka", null, null, Tckn2), ct));
        second.Error.ShouldBe(ContactErrors.DuplicateNationalId);
    }

    [Fact]
    public async Task Merge_moves_tags_and_relationships_and_redirects_old_id()
    {
        RequiresDatabase.Check();
        var primary = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Mehmet", "Kaya", "mehmet@example.org", null, null), ct).Value());
        var duplicate = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Mehmet", "Kaya", null, "0555 123 45 67", null), ct).Value());
        var org = await Scoped<ContactService, Guid>(s => s.CreateOrganizationAsync(new CreateOrganizationRequest("Kaya Ltd", null, null, null), ct).Value());
        await factory.WithScopeAsync(sp => sp.GetRequiredService<TaxonomyService>().TagContactAsync(duplicate, "Bağışçı", ct));
        await Scoped<ContactService, Guid>(s => s.AddRelationshipAsync(duplicate, new CreateRelationshipRequest(org, RelationshipType.EmployeeOf, true, "Müdür", null), ct).Value());

        var duplicates = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactService>().FindDuplicatesAsync("Mehmet Kaya", null, null, null, primary, ct));
        duplicates.ShouldContain(d => d.Id == duplicate);

        await Scoped<ContactService, Guid>(s => s.MergeAsync(primary, duplicate, ct).Value());

        var merged = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactService>().GetAsync(primary, ct));
        merged!.Phone.ShouldBe("0555 123 45 67");
        merged.Tags.ShouldContain(t => t.Name == "Bağışçı");
        merged.Relationships.ShouldContain(r => r.OtherContactId == org && r.Description == "Müdür");

        var resolved = await factory.WithScopeAsync(sp => sp.GetRequiredService<IContactDirectory>().FindAsync(duplicate, ct));
        resolved!.Id.ShouldBe(primary);
    }

    [Fact]
    public async Task Csv_import_creates_contacts_and_reports_bad_rows()
    {
        RequiresDatabase.Check();
        const string csv = """
            Tür;Ad;Soyad;Kurum/Hane;E-posta;Telefon;Kimlik no;Doğum tarihi;Web;İl;İlçe;Adres;Posta kodu;Etiketler
            Kişi;Zeynep;Demir;;zeynep@example.org;0532 000 00 01;;01.02.1990;;Ankara;Çankaya;Kızılay;06420;Gönüllü, Bülten
            Kişi;Hatalı;Kimlik;;;;12345678901;;;;;;;
            Kurum;;;"Umut; Derneği";info@umut.org;;1234567890;;https://umut.org;İstanbul;;;;
            Kişi;Zeynep;Tekrar;;zeynep@example.org;;;;;;;;;
            """;

        var result = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactTransferService>().ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), ct));

        result.IsSuccess.ShouldBeTrue();
        result.Value!.Created.ShouldBe(2);
        result.Value.Errors.Select(e => e.Row).ShouldBe([3, 5]);

        var page = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactService>().ListAsync(new ContactFilter("zeynep"), 1, 10, ct));
        var zeynep = page.Items.ShouldHaveSingleItem();
        zeynep.City.ShouldBe("Ankara");
        zeynep.Tags.Select(t => t.Name).ShouldBe(["Bülten", "Gönüllü"], ignoreOrder: true);

        var export = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactTransferService>().ExportAsync(new ContactFilter("umut"), false, ct));
        var text = Encoding.UTF8.GetString(export);
        text.ShouldContain("\"Umut; Derneği\"");
        text.ShouldNotContain("1234567890");
    }

    [Fact]
    public async Task Search_matches_phone_regardless_of_format()
    {
        RequiresDatabase.Check();
        await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Telefon", "Arama", null, "+90 (542) 765 43 21", null), ct).Value());
        var found = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactService>().ListAsync(new ContactFilter("0542 7654321"), 1, 10, ct));
        found.Items.ShouldContain(c => c.DisplayName == "Telefon Arama");
    }

    private Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull =>
        factory.WithScopeAsync(sp => action(sp.GetRequiredService<TService>()));
}

internal static class ResultTestExtensions
{
    public static async Task<T> Value<T>(this Task<Arma.SharedKernel.Domain.Result<T>> task)
    {
        var result = await task;
        result.Error.ShouldBeNull();
        return result.Value!;
    }
}
