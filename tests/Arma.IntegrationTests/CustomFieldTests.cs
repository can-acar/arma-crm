using System.Text;
using Arma.SharedKernel.Domain;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Contacts.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class CustomFieldTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Values_are_saved_searched_and_cleared_when_the_field_is_deleted()
    {
        RequiresDatabase.Check();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var size = await Field(new CustomFieldInput("Hane büyüklüğü " + suffix, CustomFieldType.Number, [], ContactType.Person, null, 1, true));
        var city = await Field(new CustomFieldInput("Bölge " + suffix, CustomFieldType.Choice, ["Kuzey", "Güney"], null, null, 2, true));
        var since = await Field(new CustomFieldInput("Gönüllülük başlangıcı " + suffix, CustomFieldType.Date, [], null, null, 3, true));
        var kvkk = await Field(new CustomFieldInput("Fotoğraf izni " + suffix, CustomFieldType.YesNo, [], null, null, 4, true));
        var note = await Field(new CustomFieldInput("Not " + suffix, CustomFieldType.Text, [], null, null, 5, true));

        var ali = await Person("Ali");
        var ayse = await Person("Ayşe");
        (await Scoped<CustomFieldService, Result<Guid>>(s => s.SaveValuesAsync(ali, new Dictionary<Guid, string?>
        {
            [size] = "5", [city] = "kuzey", [since] = "01.03.2026", [kvkk] = "evet", [note] = "Kış yardımı alıyor",
        }, ct))).IsSuccess.ShouldBeTrue();
        await Scoped<CustomFieldService, Guid>(s => s.SaveValuesAsync(ayse, new Dictionary<Guid, string?> { [size] = "2", [city] = "Güney", [kvkk] = "hayır" }, ct).Value());

        var invalid = await Scoped<CustomFieldService, Result<Guid>>(s => s.SaveValuesAsync(ali, new Dictionary<Guid, string?> { [size] = "beş" }, ct));
        invalid.Error!.Code.ShouldBe("custom_fields.value");

        var values = await Scoped<CustomFieldService, IReadOnlyList<CustomFieldValue>>(s => s.ValuesAsync(ali, ct));
        values.Single(v => v.FieldId == city).Value.ShouldBe("Kuzey");
        values.Single(v => v.FieldId == since).Display.ShouldBe("01.03.2026");
        values.Single(v => v.FieldId == size).Value.ShouldBe("5");

        (await Match($"custom_min:{size}", "3")).ShouldBe([ali]);
        (await Match($"custom:{city}", "Güney")).ShouldBe([ayse]);
        (await Match($"custom_since:{since}", "2026-02-01")).ShouldBe([ali]);
        (await Match($"custom_yes:{kvkk}", null)).ShouldBe([ali]);
        (await Match($"custom:{note}", "kış")).ShouldBe([ali]);

        // Değeri olan alanın türü değiştirilemez.
        var retype = await Scoped<CustomFieldService, Result<Guid>>(s => s.SaveAsync(size, new CustomFieldInput("Hane büyüklüğü " + suffix, CustomFieldType.Text, [], null, null, 1, true), ct));
        retype.IsSuccess.ShouldBeFalse();
        var list = await Scoped<CustomFieldService, IReadOnlyList<CustomFieldView>>(s => s.ListAsync(false, ct));
        list.Single(f => f.Id == size).UsedCount.ShouldBe(2);

        await Scoped<CustomFieldService, Guid>(s => s.DeleteAsync(size, ct).Value());
        (await Scoped<CustomFieldService, IReadOnlyList<CustomFieldValue>>(s => s.ValuesAsync(ali, ct))).ShouldNotContain(v => v.FieldId == size);
        var stored = await Scoped<ContactService, ContactDetail?>(s => s.GetAsync(ali, ct));
        stored.ShouldNotBeNull();
    }

    [Fact]
    public async Task Csv_export_and_import_carry_custom_field_columns()
    {
        RequiresDatabase.Check();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var label = "Üye no " + suffix;
        var field = await Field(new CustomFieldInput(label, CustomFieldType.Number, [], null, null, 1, true));
        var last = "Ekalan" + new string(suffix.Where(char.IsLetter).DefaultIfEmpty('a').ToArray());

        var csv = $"""
            Tür;Ad;Soyad;{label.ToUpper(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))}
            Kişi;Selin;{last};1.250
            Kişi;Hatalı;{last};abc
            """;
        var result = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactTransferService>().ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), ct));
        result.Value!.Created.ShouldBe(1);
        result.Value.Errors.Single().Row.ShouldBe(3);

        (await Match($"custom_min:{field}", "1250")).Count.ShouldBe(1);

        var export = Encoding.UTF8.GetString(await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactTransferService>().ExportAsync(new ContactFilter(last), false, ct)));
        export.ShouldContain(label);
        export.ShouldContain("1.250");
    }

    private async Task<Guid> Field(CustomFieldInput input) => await Scoped<CustomFieldService, Guid>(s => s.SaveAsync(null, input, ct).Value());

    private Task<Guid> Person(string first) =>
        Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest(first, "Ekalan", null, null, null), ct).Value());

    private async Task<IReadOnlyCollection<Guid>> Match(string key, string? value) =>
        (await Scoped<CustomFieldService, CriterionMatch?>(s => s.MatchAsync(key, value, ct)))!.ContactIds;

    private Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull =>
        factory.WithScopeAsync(sp => action(sp.GetRequiredService<TService>()));
}
