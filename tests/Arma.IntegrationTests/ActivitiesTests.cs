using System.Net;
using Arma.Modules.Activities.Application;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Activities.Domain;
using Arma.Modules.Contacts.Application;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace Arma.IntegrationTests;

public sealed class ActivitiesTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Task_lifecycle_and_contact_timeline()
    {
        RequiresDatabase.Check();
        var contact = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Selin", "Ak", null, null, null), ct).Value());

        await Scoped<ActivityService, Guid>(s => s.CreateAsync(new SaveActivityRequest(ActivityKind.Call, "Teşekkür araması", "Bağış için aradık", DateTimeOffset.UtcNow.AddHours(-1), null, null, [contact]), ct).Value());
        var task = await Scoped<ActivityService, Guid>(s => s.CreateAsync(new SaveActivityRequest(ActivityKind.Task, "Makbuz gönder", null, null, DateTimeOffset.UtcNow.AddDays(-1), null, [contact]), ct).Value());

        var (open, history) = await Scoped<ActivityService, (IReadOnlyList<ActivityView>, IReadOnlyList<ActivityView>)>(s => s.TimelineAsync(contact, cancellationToken: ct));
        open.ShouldHaveSingleItem().ShouldSatisfyAllConditions(a => a.Id.ShouldBe(task), a => a.IsOverdue.ShouldBeTrue());
        history.ShouldHaveSingleItem().Contacts.ShouldHaveSingleItem().DisplayName.ShouldBe("Selin Ak");

        await Scoped<ActivityService, Guid>(s => s.CompleteAsync(task, ct).Value());
        (open, history) = await Scoped<ActivityService, (IReadOnlyList<ActivityView>, IReadOnlyList<ActivityView>)>(s => s.TimelineAsync(contact, cancellationToken: ct));
        open.ShouldBeEmpty();
        history.Count.ShouldBe(2);
        history.Single(a => a.Id == task).Status.ShouldBe(ActivityStatus.Done);
    }

    [Fact]
    public async Task Unknown_contact_or_inactive_user_is_rejected()
    {
        RequiresDatabase.Check();
        var unknownContact = await factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>()
            .CreateAsync(new SaveActivityRequest(ActivityKind.Note, "Not", null, null, null, null, [Guid.NewGuid()]), ct));
        unknownContact.Error!.Code.ShouldBe("activities.contact_not_found");

        var unknownUser = await factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>()
            .CreateAsync(new SaveActivityRequest(ActivityKind.Task, "Görev", null, null, null, Guid.NewGuid(), []), ct));
        unknownUser.Error!.Code.ShouldBe("activities.user_not_found");
    }

    [Fact]
    public async Task Merging_contacts_moves_activities_to_primary_record()
    {
        RequiresDatabase.Check();
        var primary = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Deniz", "Yıldız", "deniz@example.org", null, null), ct).Value());
        var duplicate = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Deniz", "Yıldız", null, null, null), ct).Value());
        var both = await Scoped<ActivityService, Guid>(s => s.CreateAsync(new SaveActivityRequest(ActivityKind.Meeting, "Ortak toplantı", null, null, null, null, [primary, duplicate]), ct).Value());
        var onlyDuplicate = await Scoped<ActivityService, Guid>(s => s.CreateAsync(new SaveActivityRequest(ActivityKind.Note, "Eski not", null, null, null, null, [duplicate]), ct).Value());

        await Scoped<ContactService, Guid>(s => s.MergeAsync(primary, duplicate, ct).Value());

        // Olay kalıcı kuyruktan arka planda işlenir.
        var history = await EventuallyAsync(
            () => Scoped<ActivityService, IReadOnlyList<ActivityView>>(async s => (await s.TimelineAsync(primary, cancellationToken: ct)).History),
            h => h.Any(a => a.Id == onlyDuplicate));

        history.Select(a => a.Id).ShouldBe([both, onlyDuplicate], ignoreOrder: true);
        history.Single(a => a.Id == both).Contacts.ShouldHaveSingleItem().Id.ShouldBe(primary);
    }

    [Fact]
    public async Task Timeline_entries_from_other_modules_are_idempotent()
    {
        RequiresDatabase.Check();
        var contact = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Bağışçı", "Kişi", null, null, null), ct).Value());

        await factory.WithScopeAsync(async sp =>
        {
            var bus = sp.GetRequiredService<IMessageBus>();
            await bus.PublishAsync(new RecordTimelineEntry([contact], ActivityKind.System, "Bağış alındı: 100 TL", null, DateTimeOffset.UtcNow, "Test", "gift-1"));
            await bus.PublishAsync(new RecordTimelineEntry([contact], ActivityKind.System, "Bağış alındı: 150 TL", null, DateTimeOffset.UtcNow, "Test", "gift-1"));
            return true;
        });

        var history = await EventuallyAsync(
            () => Scoped<ActivityService, IReadOnlyList<ActivityView>>(async s => (await s.TimelineAsync(contact, cancellationToken: ct)).History),
            h => h.Any(a => a.Subject.Contains("150")));

        var entry = history.ShouldHaveSingleItem();
        entry.IsSystem.ShouldBeTrue();
        var edit = await factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>()
            .UpdateAsync(entry.Id, new SaveActivityRequest(ActivityKind.System, "Değişti", null, null, null, null, [contact]), ct));
        edit.Error!.Code.ShouldBe("activities.system");
    }

    [Fact]
    public async Task Api_requires_authentication()
    {
        RequiresDatabase.Check();
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync("/api/v1/activities", ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
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
