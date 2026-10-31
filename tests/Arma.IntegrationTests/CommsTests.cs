using Arma.Modules.Activities.Application;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Comms.Application;
using Arma.Modules.Comms.Contracts;
using Arma.Modules.Comms.Domain;
using Arma.Modules.Comms.Senders;
using Arma.Modules.Contacts.Application;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Arma.IntegrationTests;

public sealed class CommsTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Commercial_email_requires_consent_and_carries_unsubscribe_link()
    {
        RequiresDatabase.Check();
        var contact = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Elif", "Su", "elif@example.org", null, null), ct).Value());
        var request = new SendToContactRequest(contact, Channel.Email, "Kampanya {{ad}}", "Merhaba {{ad}}, yeni kampanyamız başladı.", true);

        var refused = await factory.WithScopeAsync(sp => sp.GetRequiredService<MessageService>().SendToContactAsync(request, ct));
        refused.Error!.Code.ShouldBe("messages.no_consent");

        await Scoped<ConsentService, Guid>(s => s.SetAsync(contact, Channel.Email, ConsentStatus.Approved, "manual", ct).Value());
        await Scoped<MessageService, Guid>(s => s.SendToContactAsync(request, ct).Value());

        var history = await EventuallyAsync(
            () => Scoped<MessageService, IReadOnlyList<MessageView>>(s => s.ForContactAsync(contact, cancellationToken: ct)),
            h => h.Any(m => m.Status == MessageStatus.Sent));
        history.ShouldHaveSingleItem().Subject.ShouldBe("Kampanya Elif");

        var sent = Sink.Sent.Single(m => m.To == "elif@example.org");
        sent.Body.ShouldStartWith("Merhaba Elif,");
        sent.Body.ShouldContain("https://crm.example.org/abonelik/iptal?t=");

        var (_, timeline) = await EventuallyAsync(
            () => Scoped<ActivityService, (IReadOnlyList<ActivityView>, IReadOnlyList<ActivityView>)>(s => s.TimelineAsync(contact, cancellationToken: ct)),
            t => t.Item2.Count > 0);
        timeline.ShouldHaveSingleItem().ShouldSatisfyAllConditions(a => a.Kind.ShouldBe(ActivityKind.Email), a => a.Subject.ShouldBe("E-posta: Kampanya Elif"));
    }

    [Fact]
    public async Task Unsubscribe_token_rejects_consent_and_tampered_token_is_refused()
    {
        RequiresDatabase.Check();
        var contact = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Kaan", "Er", "kaan@example.org", "0532 444 55 66", null), ct).Value());
        await Scoped<ConsentService, Guid>(s => s.SetAsync(contact, Channel.Sms, ConsentStatus.Approved, "form", ct).Value());

        var token = await factory.WithScopeAsync(sp => Task.FromResult(sp.GetRequiredService<ConsentService>().UnsubscribeToken(contact, Channel.Sms)));
        (await Scoped<ConsentService, bool>(s => s.UnsubscribeAsync(token[..^2] + "xx", ct))).ShouldBeFalse();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var oneClick = await client.PostAsync($"/api/v1/unsubscribe?t={Uri.EscapeDataString(token)}",
            new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")]), ct);
        oneClick.IsSuccessStatusCode.ShouldBeTrue();
        (await Scoped<ConsentService, bool>(s => s.UnsubscribeAsync(token, ct))).ShouldBeTrue();

        var consents = await Scoped<ConsentService, IReadOnlyList<ConsentView>>(s => s.ForContactAsync(contact, ct));
        consents.Single(c => c.Channel == Channel.Sms).ShouldSatisfyAllConditions(
            c => c.Status.ShouldBe(ConsentStatus.Rejected),
            c => c.Address.ShouldBe("5324445566"),
            c => c.Source.ShouldBe("unsubscribe"));
        consents.Single(c => c.Channel == Channel.Email).Status.ShouldBe(ConsentStatus.Unknown);
    }

    [Fact]
    public async Task Mailing_sends_only_to_reachable_contacts_and_completes()
    {
        RequiresDatabase.Check();
        var approved = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Ada", "Onaylı", "ada@example.org", null, null), ct).Value());
        var noConsent = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Bora", "İzinsiz", "bora@example.org", null, null), ct).Value());
        var noAddress = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Cem", "Adressiz", null, null, null), ct).Value());
        Guid tagId = default;
        foreach (var id in new[] { approved, noConsent, noAddress })
        {
            var tag = await factory.WithScopeAsync(sp => sp.GetRequiredService<TaxonomyService>().TagContactAsync(id, "Bülten", ct));
            tagId = tag.Value!.Id;
        }

        await Scoped<ConsentService, Guid>(s => s.SetAsync(approved, Channel.Email, ConsentStatus.Approved, "form", ct).Value());

        var preview = await Scoped<MailingService, AudiencePreview>(s => s.PreviewAsync(Channel.Email, true, null, tagId, ct));
        preview.ShouldBe(new AudiencePreview(3, 1, 1, 1));

        var mailing = await Scoped<MailingService, Guid>(s => s.SaveAsync(null, new SaveMailingRequest("Ekim bülteni", Channel.Email, "Ekim bülteni", "Merhaba {{ad}}", true, null, tagId), ct).Value());
        await Scoped<MailingService, Guid>(s => s.StartAsync(mailing, ct).Value());
        (await factory.WithScopeAsync(sp => sp.GetRequiredService<MailingService>().StartAsync(mailing, ct))).IsSuccess.ShouldBeFalse();

        var result = await EventuallyAsync(
            () => Scoped<MailingService, MailingView?>(s => s.GetAsync(mailing, ct)),
            m => m?.Status == MailingStatus.Completed);
        result!.ShouldSatisfyAllConditions(
            m => m.Status.ShouldBe(MailingStatus.Completed),
            m => m.Sent.ShouldBe(1),
            m => m.Skipped.ShouldBe(2));
        Sink.Sent.ShouldContain(m => m.To == "ada@example.org" && m.Body.StartsWith("Merhaba Ada"));
        Sink.Sent.ShouldNotContain(m => m.To == "bora@example.org");
    }

    [Fact]
    public async Task Transactional_email_from_another_module_ignores_consent()
    {
        RequiresDatabase.Check();
        var contact = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Deniz", "Makbuz", "deniz.makbuz@example.org", null, null), ct).Value());
        await Scoped<ConsentService, Guid>(s => s.SetAsync(contact, Channel.Email, ConsentStatus.Rejected, "manual", ct).Value());

        await factory.WithScopeAsync(async sp =>
        {
            await sp.GetRequiredService<IMessageBus>().PublishAsync(new SendTransactionalEmail(contact, "Makbuzunuz", "Sayın {{adsoyad}}, makbuzunuz ektedir."));
            return true;
        });

        var history = await EventuallyAsync(
            () => Scoped<MessageService, IReadOnlyList<MessageView>>(s => s.ForContactAsync(contact, cancellationToken: ct)),
            h => h.Any(m => m.Status == MessageStatus.Sent));
        history.ShouldHaveSingleItem().Status.ShouldBe(MessageStatus.Sent);
        var sent = Sink.Sent.Single(m => m.To == "deniz.makbuz@example.org");
        sent.Body.ShouldBe("Sayın Deniz Makbuz, makbuzunuz ektedir.");
    }

    [Fact]
    public async Task Merging_contacts_moves_consents_and_messages()
    {
        RequiresDatabase.Check();
        var primary = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Ece", "Birleşik", "ece@example.org", null, null), ct).Value());
        var duplicate = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Ece", "Birleşik", "ece2@example.org", "0533 111 00 00", null), ct).Value());
        await Scoped<ConsentService, Guid>(s => s.SetAsync(duplicate, Channel.Sms, ConsentStatus.Approved, "form", ct).Value());

        await Scoped<ContactService, Guid>(s => s.MergeAsync(primary, duplicate, ct).Value());

        var consents = await EventuallyAsync(
            () => Scoped<ConsentService, IReadOnlyList<ConsentView>>(s => s.ForContactAsync(primary, ct)),
            c => c.Any(x => x.Channel == Channel.Sms && x.Status == ConsentStatus.Approved));
        consents.Single(c => c.Channel == Channel.Sms).Status.ShouldBe(ConsentStatus.Approved);
    }

    private TestMessageSink Sink => factory.Services.GetRequiredService<TestMessageSink>();

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
