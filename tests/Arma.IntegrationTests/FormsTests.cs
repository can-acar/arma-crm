using Arma.Modules.Activities.Application;
using Arma.Modules.Comms.Application;
using Arma.Modules.Comms.Domain;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Forms.Application;
using Arma.Modules.Forms.Domain;
using Arma.Modules.Privacy.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class FormsTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    private static readonly FormField[] ContactFields =
    [
        new("", "Ad", FieldType.Text, true, [], FieldMap.FirstName, null),
        new("", "Soyad", FieldType.Text, true, [], FieldMap.LastName, null),
        new("", "E-posta", FieldType.Email, true, [], FieldMap.Email, null),
        new("", "Telefon", FieldType.Phone, false, [], FieldMap.Phone, null),
        new("", "Uygun gün", FieldType.Select, true, ["Hafta içi", "Hafta sonu"], null, null),
    ];

    [Fact]
    public async Task Submission_creates_contact_and_applies_tag_consents_and_timeline()
    {
        RequiresDatabase.Check();
        var notice = await Scoped<NoticeService, Guid>(s => s.SaveAsync(null, new SaveNoticeRequest("Gönüllü açık rıza", "gonullu-riza", true, "Metin"), ct).Value());
        await Scoped<NoticeService, int>(s => s.PublishAsync(notice, ct).Value());
        var settings = new FormSettings("Gönüllü başvurusu", "gonullu", "Gönüllü olun", null, null, true, "Gönüllü adayı", null, true, false, "gonullu-riza", null);
        var formId = await Scoped<FormService, Guid>(s => s.SaveAsync(null, settings, ContactFields, ct).Value());
        var form = (await Scoped<FormService, FormView?>(s => s.GetAsync(formId, ct)))!;
        form.CreatesContact.ShouldBeTrue();
        var keys = form.Fields.Select(f => f.Key).ToArray();
        keys.ShouldBe(["f1", "f2", "f3", "f4", "f5"]);

        Dictionary<string, string?> Values(string day) => new() { ["f1"] = "Ayşe", ["f2"] = "Gönüllü", ["f3"] = "ayse.gonullu@example.org", ["f4"] = "0532 111 22 33", ["f5"] = day };

        var invalid = await Submit("gonullu", new FormInput(Values("Her gün"), true, false, true, null));
        invalid.Error!.Code.ShouldBe("forms.field_invalid");
        var noConsent = await Submit("gonullu", new FormInput(Values("Hafta sonu"), true, false, false, null));
        noConsent.Error!.Code.ShouldBe("forms.consent_required");
        var missing = await Submit("gonullu", new FormInput(new Dictionary<string, string?> { ["f1"] = "Ayşe" }, false, false, true, null));
        missing.Error!.Code.ShouldBe("forms.field_required");

        (await Submit("gonullu", new FormInput(Values("Hafta sonu"), true, false, true, "10.0.0.5"))).IsSuccess.ShouldBeTrue();

        var submission = (await Scoped<FormService, IReadOnlyList<SubmissionView>>(s => s.SubmissionsAsync(formId, cancellationToken: ct))).Single();
        var contactId = submission.ContactId!.Value;
        submission.Values.Single(v => v.Label == "Uygun gün").Value.ShouldBe("Hafta sonu");

        var contact = await EventuallyAsync(() => Scoped<ContactService, ContactDetail?>(s => s.GetAsync(contactId, ct)), c => c!.Tags.Count > 0);
        contact!.ShouldSatisfyAllConditions(c => c.Email.ShouldBe("ayse.gonullu@example.org"), c => c.Tags.Single().Name.ShouldBe("Gönüllü adayı"));
        var email = await EventuallyAsync(
            () => Scoped<ConsentService, IReadOnlyList<ConsentView>>(s => s.ForContactAsync(contactId, ct)),
            list => list.Any(c => c.Channel == Channel.Email && c.Status == ConsentStatus.Approved));
        email.Single(c => c.Channel == Channel.Email).Status.ShouldBe(ConsentStatus.Approved);
        var consent = await EventuallyAsync(
            () => Scoped<ConsentRecordService, IReadOnlyList<ConsentState>>(s => s.ForContactAsync(contactId, ct)),
            list => list.Any(c => c.NoticeId == notice && c.Granted == true));
        consent.Single(c => c.NoticeId == notice).Granted.ShouldBe(true);
        var timeline = await EventuallyAsync(
            () => factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>().TimelineAsync(contactId, cancellationToken: ct)),
            t => t.History.Count > 0);
        timeline.History.ShouldContain(a => a.Subject.Contains("Gönüllü olun"));

        // Aynı e-postayla ikinci yanıt yeni kişi açmaz.
        (await Submit("gonullu", new FormInput(Values("Hafta içi"), false, false, true, null))).IsSuccess.ShouldBeTrue();
        var all = await Scoped<FormService, IReadOnlyList<SubmissionView>>(s => s.SubmissionsAsync(formId, cancellationToken: ct));
        all.Select(s => s.ContactId).Distinct().ShouldHaveSingleItem().ShouldBe(contactId);

        var csv = System.Text.Encoding.UTF8.GetString(await Scoped<FormService, byte[]>(s => s.SubmissionsCsvAsync(formId, ct)));
        csv.ShouldContain("Hafta içi");
    }

    [Fact]
    public async Task Closed_form_rejects_submissions_and_settings_are_validated()
    {
        RequiresDatabase.Check();
        var fields = new[] { new FormField("", "Mesajınız", FieldType.TextArea, true, [], null, null) };
        var needsContact = await factory.WithScopeAsync(sp => sp.GetRequiredService<FormService>().SaveAsync(null,
            new FormSettings("İletişim", "iletisim", "Bize yazın", null, null, true, "Etiket", null, false, false, null, null), fields, ct));
        needsContact.Error!.Code.ShouldBe("forms.needs_contact");

        var badSlug = await factory.WithScopeAsync(sp => sp.GetRequiredService<FormService>().SaveAsync(null,
            new FormSettings("İletişim", "İletişim Formu", "Bize yazın", null, null, true, null, null, false, false, null, null), fields, ct));
        badSlug.IsSuccess.ShouldBeFalse();

        var settings = new FormSettings("İletişim", "iletisim", "Bize yazın", null, null, false, null, null, false, false, null, null);
        var id = await Scoped<FormService, Guid>(s => s.SaveAsync(null, settings, fields, ct).Value());
        (await Scoped<FormService, FormView?>(s => s.PublishedAsync("iletisim", ct))).ShouldBeNull();
        (await Submit("iletisim", new FormInput(new Dictionary<string, string?> { ["f1"] = "Merhaba" }, false, false, false, null))).Error.ShouldBe(FormErrors.NotFound);

        await Scoped<FormService, Guid>(s => s.SaveAsync(id, settings with { IsActive = true }, fields, ct).Value());
        (await Submit("iletisim", new FormInput(new Dictionary<string, string?> { ["f1"] = "Merhaba" }, false, false, false, null))).IsSuccess.ShouldBeTrue();
        (await Scoped<FormService, IReadOnlyList<SubmissionView>>(s => s.SubmissionsAsync(id, cancellationToken: ct))).Single().ContactId.ShouldBeNull();
    }

    private Task<Arma.SharedKernel.Domain.Result<Guid>> Submit(string slug, FormInput input) =>
        factory.WithScopeAsync(sp => sp.GetRequiredService<FormService>().SubmitAsync(slug, input, ct));

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
