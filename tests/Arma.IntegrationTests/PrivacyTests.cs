using System.Text;
using Arma.Modules.Activities.Application;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Audit;
using Arma.Modules.Comms.Application;
using Arma.Modules.Comms.Domain;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Domain;
using Arma.Modules.Privacy.Application;
using Arma.Modules.Privacy.Domain;
using Arma.Modules.Privacy.Infrastructure;
using Arma.SharedKernel.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class PrivacyTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Consent_is_bound_to_published_version_and_records_are_immutable()
    {
        RequiresDatabase.Check();
        var contact = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Rıza", "Veren", null, null, null), ct).Value());
        var notice = await Scoped<NoticeService, Guid>(s => s.SaveAsync(null, new SaveNoticeRequest("Bülten açık rıza metni", "bulten-riza", true, "Sürüm 1 metni"), ct).Value());

        var unpublished = await factory.WithScopeAsync(sp => sp.GetRequiredService<ConsentRecordService>().RecordAsync(contact, notice, true, "manual", null, null, ct));
        unpublished.Error!.Code.ShouldBe("consents.not_published");

        (await Scoped<NoticeService, int>(s => s.PublishAsync(notice, ct).Value())).ShouldBe(1);
        await Scoped<ConsentRecordService, Guid>(s => s.RecordAsync(contact, notice, true, "form", "10.0.0.1", null, ct).Value());
        await Scoped<NoticeService, Guid>(s => s.SaveAsync(notice, new SaveNoticeRequest("Bülten açık rıza metni", "bulten-riza", true, "Sürüm 2 metni"), ct).Value());
        (await Scoped<NoticeService, int>(s => s.PublishAsync(notice, ct).Value())).ShouldBe(2);

        var state = (await Scoped<ConsentRecordService, IReadOnlyList<ConsentState>>(s => s.ForContactAsync(contact, ct))).Single(s => s.NoticeId == notice);
        state.ShouldSatisfyAllConditions(s => s.Granted.ShouldBe(true), s => s.VersionNumber.ShouldBe(1), s => s.Outdated.ShouldBeTrue());

        var published = await Scoped<NoticeService, NoticeVersionView?>(s => s.PublishedAsync("bulten-riza", ct));
        published!.Text.ShouldBe("Sürüm 2 metni");

        var ex = await Should.ThrowAsync<Exception>(() => factory.WithScopeAsync(async sp =>
            await sp.GetRequiredService<PrivacyDbContext>().Database.ExecuteSqlRawAsync("UPDATE privacy.consent_records SET granted = false", ct)));
        ex.Message.ShouldContain("değiştirilemez");
    }

    [Fact]
    public async Task Request_is_due_in_30_days_and_needs_a_response_to_close()
    {
        RequiresDatabase.Check();
        var received = DateOnly.FromDateTime(DateTime.Today).AddDays(-40);
        var id = await Scoped<DataRequestService, Guid>(s => s.CreateAsync(new CreateDataRequest(RequestType.Access, RequestChannel.Email, "Başvuran Kişi", "basvuran@example.org", null, "Verilerimin kopyasını istiyorum.", received), ct).Value());

        var request = await Scoped<DataRequestService, DataRequestView?>(s => s.GetAsync(id, ct));
        request!.ShouldSatisfyAllConditions(r => r.DueOn.ShouldBe(received.AddDays(30)), r => r.IsOverdue.ShouldBeTrue());
        (await Scoped<DataRequestService, int>(s => s.OverdueCountAsync(ct))).ShouldBeGreaterThan(0);

        var noResponse = await factory.WithScopeAsync(sp => sp.GetRequiredService<DataRequestService>().CloseAsync(id, true, " ", ct));
        noResponse.Error!.Code.ShouldBe("requests.response_required");
        await Scoped<DataRequestService, Guid>(s => s.CloseAsync(id, true, "Verileriniz e-postayla gönderildi.", ct).Value());
        (await Scoped<DataRequestService, DataRequestView?>(s => s.GetAsync(id, ct)))!.ShouldSatisfyAllConditions(
            r => r.Status.ShouldBe(RequestStatus.Completed), r => r.IsOverdue.ShouldBeFalse());
    }

    [Fact]
    public async Task Export_collects_data_from_every_module_and_anonymize_erases_it()
    {
        RequiresDatabase.Check();
        var contact = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Silinecek", "Kişi", "silinecek@example.org", "0532 999 88 77", "10000000146"), ct).Value());
        await factory.WithScopeAsync(sp => sp.GetRequiredService<TaxonomyService>().TagContactAsync(contact, "Gönüllü", ct));
        var donation = await Scoped<DonationService, Guid>(s => s.RecordAsync(new SaveDonationRequest(contact, 500m, "TRY", DateOnly.FromDateTime(DateTime.Today), null, PaymentMethod.Cash, null, "Annesinin adına", null), ct).Value());
        await Scoped<ActivityService, Guid>(s => s.CreateAsync(new SaveActivityRequest(ActivityKind.Note, "Sağlık durumu", "Ameliyat oldu, aranmasın", null, null, null, [contact]), ct).Value());
        await Scoped<ConsentService, Guid>(s => s.SetAsync(contact, Channel.Email, ConsentStatus.Approved, "form", ct).Value());
        await Scoped<MessageService, Guid>(s => s.SendToContactAsync(new SendToContactRequest(contact, Channel.Email, "Merhaba", "Merhaba {{ad}}", false), ct).Value());

        // Arka plandaki gönderim ve zaman çizelgesi kayıtları bitsin; anonimleştirme sonrasında kişi verisi geri gelmemeli.
        await EventuallyAsync(
            () => Scoped<ActivityService, (IReadOnlyList<ActivityView>, IReadOnlyList<ActivityView>)>(s => s.TimelineAsync(contact, cancellationToken: ct)),
            t => t.Item2.Count >= 3);

        var export = await Scoped<PersonalDataService, byte[]>(s => s.ExportAsync(contact, ct).Value());
        var json = Encoding.UTF8.GetString(export);
        foreach (var expected in new[] { "silinecek@example.org", "10000000146", "Gönüllü", "500,00 TL", "Ameliyat oldu", "Merhaba Silinecek" })
        {
            json.ShouldContain(expected);
        }

        await Scoped<PersonalDataService, Guid>(s => s.AnonymizeAsync(contact, null, ct).Value());

        var summary = await Scoped<IContactDirectory, ContactSummary?>(d => d.FindAsync(contact, ct));
        summary!.ShouldSatisfyAllConditions(c => c.DisplayName.ShouldStartWith("Anonim kişi"), c => c.Email.ShouldBeNull(), c => c.Phone.ShouldBeNull());
        var card = await Scoped<ContactService, ContactDetail?>(s => s.GetAsync(contact, ct));
        card!.Tags.ShouldBeEmpty();

        var gift = await Scoped<DonationService, DonationView?>(s => s.GetAsync(donation, ct));
        gift!.ShouldSatisfyAllConditions(d => d.Amount.ShouldBe(500m), d => d.Note.ShouldBeNull());

        var (_, history) = await Scoped<ActivityService, (IReadOnlyList<ActivityView>, IReadOnlyList<ActivityView>)>(s => s.TimelineAsync(contact, cancellationToken: ct));
        history.ShouldNotContain(a => a.Subject == "Sağlık durumu" || a.Body != null && a.Body.Contains("Ameliyat"));

        var messages = await Scoped<MessageService, IReadOnlyList<MessageView>>(s => s.ForContactAsync(contact, cancellationToken: ct));
        messages.ShouldAllBe(m => m.To == null && !m.Body.Contains("Silinecek"));

        var again = await factory.WithScopeAsync(sp => sp.GetRequiredService<PersonalDataService>().AnonymizeAsync(contact, null, ct));
        again.Error!.Code.ShouldBe("privacy.already_anonymized");

        var audit = await Scoped<IAuditLog, IReadOnlyList<AuditRecord>>(a => a.ForEntityAsync("Contact", contact.ToString(), ct));
        audit.ShouldContain(r => r.Action == AuditAction.Anonymized);
        audit.ShouldContain(r => r.Action == AuditAction.Exported);
    }

    [Fact]
    public async Task Merging_contacts_moves_consent_records_and_requests()
    {
        RequiresDatabase.Check();
        var primary = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Birleşen", "Asıl", null, null, null), ct).Value());
        var duplicate = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Birleşen", "Asıl", null, null, null), ct).Value());
        var notice = await Scoped<NoticeService, Guid>(s => s.SaveAsync(null, new SaveNoticeRequest("Fotoğraf rızası", "fotograf-riza", true, "Metin"), ct).Value());
        await Scoped<NoticeService, int>(s => s.PublishAsync(notice, ct).Value());
        await Scoped<ConsentRecordService, Guid>(s => s.RecordAsync(duplicate, notice, true, "manual", null, null, ct).Value());
        var request = await Scoped<DataRequestService, Guid>(s => s.CreateAsync(new CreateDataRequest(RequestType.Information, RequestChannel.InPerson, "Birleşen Asıl", null, null, "Bilgi"), ct).Value());
        await Scoped<DataRequestService, Guid>(s => s.UpdateAsync(request, duplicate, true, ct).Value());

        await Scoped<ContactService, Guid>(s => s.MergeAsync(primary, duplicate, ct).Value());

        var states = await EventuallyAsync(
            () => Scoped<ConsentRecordService, IReadOnlyList<ConsentState>>(s => s.ForContactAsync(primary, ct)),
            x => x.Any(c => c.NoticeId == notice && c.Granted == true));
        states.Single(c => c.NoticeId == notice).Granted.ShouldBe(true);
        (await Scoped<DataRequestService, DataRequestView?>(s => s.GetAsync(request, ct)))!.ContactId.ShouldBe(primary);
    }

    [Fact]
    public async Task Organizations_are_not_anonymized()
    {
        RequiresDatabase.Check();
        var org = await Scoped<ContactService, Guid>(s => s.CreateOrganizationAsync(new CreateOrganizationRequest("Örnek Vakfı", null, null, null), ct).Value());
        var result = await factory.WithScopeAsync(sp => sp.GetRequiredService<PersonalDataService>().AnonymizeAsync(org, null, ct));
        result.Error!.Code.ShouldBe("privacy.organization");
    }

    private async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var value = await read();
            if (done(value) || DateTime.UtcNow > deadline)
            {
                return value;
            }

            await Task.Delay(250, ct);
        }
    }

    private Task<TResult> Scoped<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull =>
        factory.WithScopeAsync(sp => action(sp.GetRequiredService<TService>()));
}
