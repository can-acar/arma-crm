using System.Security.Cryptography;
using Arma.Infrastructure.Security;
using Arma.Modules.Contacts;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Domain;
using Arma.Modules.Contacts.Infrastructure;
using Arma.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arma.IntegrationTests;

/// <summary>Hassas alanların şifrelenmesi ve blind index ile aranması.</summary>
public sealed class SensitiveDataTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    // Algoritmaya uygun, bu sınıfa özgü örnek TCKN'ler (sınıfın veritabanı testler arasında paylaşılır).
    private const string StoredTckn = "12345678950";
    private const string DuplicateTckn = "23456789138";
    private const string LegacyTckn = "34567891238";

    private static readonly SensitiveDataProtector Protector = new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Encryption_round_trips_and_is_randomized()
    {
        var first = Protector.Encrypt(StoredTckn);
        var second = Protector.Encrypt(StoredTckn);

        first.ShouldStartWith(SensitiveDataProtector.Prefix);
        first.ShouldNotContain(StoredTckn);
        first.ShouldNotBe(second);
        Protector.Decrypt(first).ShouldBe(StoredTckn);
        Protector.Decrypt(second).ShouldBe(StoredTckn);
    }

    [Fact]
    public void Plaintext_from_before_encryption_is_read_as_is() =>
        Protector.Decrypt(StoredTckn).ShouldBe(StoredTckn);

    [Fact]
    public void Tampered_value_or_wrong_key_is_rejected()
    {
        var encrypted = Protector.Encrypt(StoredTckn);
        var bytes = Convert.FromBase64String(encrypted[SensitiveDataProtector.Prefix.Length..]);
        bytes[^1] ^= 1;

        Should.Throw<CryptographicException>(() => Protector.Decrypt(SensitiveDataProtector.Prefix + Convert.ToBase64String(bytes)));
        Should.Throw<CryptographicException>(() => new SensitiveDataProtector(RandomNumberGenerator.GetBytes(32)).Decrypt(encrypted));
    }

    [Fact]
    public void Hash_is_deterministic_and_depends_on_the_key()
    {
        Protector.Hash(StoredTckn).ShouldBe(Protector.Hash(StoredTckn));
        Protector.Hash(StoredTckn).ShouldNotBe(Protector.Hash(DuplicateTckn));
        Protector.Hash(StoredTckn).ShouldNotBe(new SensitiveDataProtector(RandomNumberGenerator.GetBytes(32)).Hash(StoredTckn));
        Protector.Hash(StoredTckn).Length.ShouldBe(64);
    }

    [Fact]
    public void Production_requires_a_valid_key()
    {
        Should.Throw<InvalidOperationException>(() => Create(null, Environments.Production)).Message.ShouldContain("eksik");
        Should.Throw<InvalidOperationException>(() => Create("bm90LWEta2V5", Environments.Production)).Message.ShouldContain("32 bayt");
        Should.Throw<InvalidOperationException>(() => Create("%%%", Environments.Production)).Message.ShouldContain("base64");
        Create(null, Environments.Development).Hash("x").Length.ShouldBe(64);
    }

    [Fact]
    public async Task National_id_is_stored_encrypted_and_found_through_its_hash()
    {
        RequiresDatabase.Check();
        var id = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Zeynep", "Demir", null, null, StoredTckn), ct).Value());

        var (stored, hash) = await RawAsync(id);
        stored.ShouldStartWith(SensitiveDataProtector.Prefix);
        stored.ShouldNotContain(StoredTckn);
        hash.ShouldBe(await factory.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<ISensitiveDataProtector>().Hash(StoredTckn))));

        (await Scoped<ContactService, string?>(s => s.RevealNationalIdAsync(id, ct))).ShouldBe(StoredTckn);
        var duplicates = await Scoped<ContactService, IReadOnlyList<DuplicateCandidate>>(s => s.FindDuplicatesAsync(null, null, null, StoredTckn, null, ct));
        duplicates.Single().ShouldSatisfyAllConditions(d => d.Id.ShouldBe(id), d => d.Reason.ShouldBe("Aynı kimlik numarası"));
    }

    [Fact]
    public async Task Merge_moves_the_national_id_and_its_hash_to_the_primary_record()
    {
        RequiresDatabase.Check();
        var primary = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Can", "Yıldız", "can@example.org", null, null), ct).Value());
        var duplicate = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Can", "Yıldız", null, null, DuplicateTckn), ct).Value());

        await Scoped<ContactService, Guid>(s => s.MergeAsync(primary, duplicate, ct).Value());

        (await Scoped<ContactService, string?>(s => s.RevealNationalIdAsync(primary, ct))).ShouldBe(DuplicateTckn);
        (await RawAsync(primary)).Hash.ShouldNotBeNull();
        (await RawAsync(duplicate)).ShouldBe((null, null));
    }

    [Fact]
    public async Task Migrate_encrypts_national_ids_saved_before_encryption()
    {
        RequiresDatabase.Check();
        var id = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Eski", "Kayıt", null, null, null), ct).Value());
        // Güncellemeden önceki durumu taklit eder: açık numara, özet yok.
        await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactsDbContext>().Database
            .ExecuteSqlAsync($"UPDATE contacts.contacts SET national_id = {LegacyTckn}, national_id_hash = NULL WHERE id = {id}", ct));
        (await RawAsync(id)).Stored.ShouldBe(LegacyTckn);

        await new ContactsModule().MigrateAsync(factory.Services, ct);

        var (stored, hash) = await RawAsync(id);
        stored.ShouldStartWith(SensitiveDataProtector.Prefix);
        hash.ShouldNotBeNull();
        (await Scoped<ContactService, string?>(s => s.RevealNationalIdAsync(id, ct))).ShouldBe(LegacyTckn);
        var taken = await factory.WithScopeAsync(sp => sp.GetRequiredService<ContactService>().CreatePersonAsync(new CreatePersonRequest("Başka", "Kişi", null, null, LegacyTckn), ct));
        taken.Error.ShouldBe(ContactErrors.DuplicateNationalId);
    }

    private static SensitiveDataProtector Create(string? key, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SensitiveDataProtector.ConfigurationKey] = key })
            .Build();
        return SensitiveDataProtector.Create(configuration, new HostingEnvironment { EnvironmentName = environment }, NullLogger.Instance);
    }

    private Task<(string? Stored, string? Hash)> RawAsync(Guid id) =>
        factory.WithScopeAsync(async sp =>
        {
            var row = await sp.GetRequiredService<ContactsDbContext>().Database
                .SqlQuery<RawRow>($"SELECT national_id AS stored, national_id_hash AS hash FROM contacts.contacts WHERE id = {id}")
                .SingleAsync(ct);
            return (row.Stored, row.Hash);
        });

    private Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull =>
        factory.WithScopeAsync(sp => action(sp.GetRequiredService<TService>()));

    private sealed record RawRow(string? Stored, string? Hash);
}
