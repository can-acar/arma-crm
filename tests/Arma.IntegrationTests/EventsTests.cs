using Arma.Modules.Activities.Application;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Events.Application;
using Arma.Modules.Events.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class EventsTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task Full_event_waitlists_and_cancellation_promotes_the_next_person()
    {
        RequiresDatabase.Check();
        var id = await CreateEventAsync("kontenjanli", capacity: 2);
        var people = await Task.WhenAll(Enumerable.Range(1, 3).Select(i => Person($"Kişi{i}")));

        var registrations = new List<Guid>();
        foreach (var person in people)
        {
            registrations.Add(await Scoped<EventService, Guid>(s => s.RegisterAsync(id, person, null, "staff", null, ct).Value()));
        }

        var list = await Participants(id);
        list.Single(p => p.ContactId == people[2]).Status.ShouldBe(RegistrationStatus.Waitlisted);
        var duplicate = await factory.WithScopeAsync(sp => sp.GetRequiredService<EventService>().RegisterAsync(id, people[0], null, "staff", null, ct));
        duplicate.Error.ShouldBe(EventErrors.AlreadyRegistered);

        await Scoped<EventService, Guid>(s => s.CancelRegistrationAsync(registrations[0], ct).Value());
        list = await Participants(id);
        list.Single(p => p.ContactId == people[2]).Status.ShouldBe(RegistrationStatus.Registered);
        list.Single(p => p.ContactId == people[0]).Status.ShouldBe(RegistrationStatus.Cancelled);
        (await Scoped<EventService, EventView?>(s => s.GetAsync(id, ct)))!.ShouldSatisfyAllConditions(e => e.Registered.ShouldBe(2), e => e.Waitlisted.ShouldBe(0));

        var timeline = await EventuallyAsync(
            () => factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>().TimelineAsync(people[2], cancellationToken: ct)),
            t => t.History.Any(a => a.Subject.StartsWith("Etkinliğe kayıt")));
        timeline.History.ShouldContain(a => a.Subject.StartsWith("Etkinliğe kayıt"));
    }

    [Fact]
    public async Task Concurrent_registrations_never_exceed_capacity()
    {
        RequiresDatabase.Check();
        var id = await CreateEventAsync("yogun-talep", capacity: 3);
        var people = await Task.WhenAll(Enumerable.Range(1, 10).Select(i => Person($"Yoğun{i}")));

        var results = await Task.WhenAll(people.Select(p => factory.WithScopeAsync(sp => sp.GetRequiredService<EventService>().RegisterAsync(id, p, null, "web", null, ct))));

        results.ShouldAllBe(r => r.IsSuccess);
        var list = await Participants(id);
        list.Count(p => p.Status == RegistrationStatus.Registered).ShouldBe(3);
        list.Count(p => p.Status == RegistrationStatus.Waitlisted).ShouldBe(7);
    }

    [Fact]
    public async Task Public_registration_uses_public_tickets_and_check_in_validates_codes()
    {
        RequiresDatabase.Check();
        var id = await CreateEventAsync("gala", capacity: null);
        var full = await Scoped<EventService, Guid>(s => s.SaveTicketAsync(id, null, "Tam", 250m, null, true, ct).Value());
        var guest = await Scoped<EventService, Guid>(s => s.SaveTicketAsync(id, null, "Davetli", 0m, 1, false, ct).Value());
        var input = new PublicRegistration("Elif", "Galaya", "elif.gala@example.org", null, full);

        var draft = await factory.WithScopeAsync(sp => sp.GetRequiredService<EventService>().RegisterPublicAsync("gala", input, ct));
        draft.Error.ShouldBe(EventErrors.NotFound);
        await Scoped<EventService, Guid>(s => s.PublishAsync(id, true, ct).Value());
        var hidden = await factory.WithScopeAsync(sp => sp.GetRequiredService<EventService>().RegisterPublicAsync("gala", input with { TicketTypeId = guest }, ct));
        hidden.Error.ShouldBe(EventErrors.TicketNotFound);

        var code = await Scoped<EventService, string>(s => s.RegisterPublicAsync("gala", input, ct).Value());
        var ticket = (await Scoped<EventService, TicketPageView?>(s => s.TicketAsync(code.ToLowerInvariant(), ct)))!;
        ticket.ShouldSatisfyAllConditions(t => t.Name.ShouldBe("Elif Galaya"), t => t.Payment.ShouldBe(PaymentState.Unpaid), t => t.Amount.ShouldBe(250m));

        // Staff can use the closed ticket; its own capacity of 1 sends the second guest to the waitlist.
        var guest1 = await Person("Davetli1");
        var guest2 = await Person("Davetli2");
        await Scoped<EventService, Guid>(s => s.RegisterAsync(id, guest1, guest, "staff", null, ct).Value());
        var waiting = await Scoped<EventService, Guid>(s => s.RegisterAsync(id, guest2, guest, "staff", null, ct).Value());
        var noTicket = await factory.WithScopeAsync(sp => sp.GetRequiredService<EventService>().RegisterAsync(id, guest2, null, "staff", null, ct));
        noTicket.Error.ShouldBe(EventErrors.TicketRequired);

        (await CheckIn(id, "YANLIS-KOD")).Error.ShouldBe(EventErrors.UnknownCode);
        var ok = await CheckIn(id, $" {code[..5]} {code[5..]} ");
        ok.Value!.ShouldSatisfyAllConditions(r => r.Name.ShouldBe("Elif Galaya"), r => r.Payment.ShouldBe(PaymentState.Unpaid));
        (await CheckIn(id, code)).Error!.Code.ShouldBe("registrations.already_checked_in");
        var waitingCode = (await Participants(id)).Single(p => p.Id == waiting).Code;
        (await CheckIn(id, waitingCode)).Error!.Code.ShouldBe("registrations.not_registered");

        (await Scoped<EventService, TicketPageView?>(s => s.TicketAsync(code, ct)))!.CheckedInAt.ShouldNotBeNull();
        (await Scoped<EventService, EventView?>(s => s.GetAsync(id, ct)))!.CheckedIn.ShouldBe(1);
    }

    [Fact]
    public async Task Reminder_goes_to_registered_people_once_the_day_before()
    {
        RequiresDatabase.Check();
        var soon = DateTimeOffset.Now.AddHours(12);
        var id = await Scoped<EventService, Guid>(s => s.SaveAsync(null, new EventDetails("Yarınki seminer", "yarin", null, null, soon, soon.AddHours(2), 1, null, null), ct).Value());
        await Scoped<EventService, Guid>(s => s.PublishAsync(id, true, ct).Value());
        var first = await Person("Hatırlatılan");
        var second = await Person("Yedekteki");
        await Scoped<EventService, Guid>(s => s.RegisterAsync(id, first, null, "staff", null, ct).Value());
        await Scoped<EventService, Guid>(s => s.RegisterAsync(id, second, null, "staff", null, ct).Value());

        (await Scoped<EventReminderJob, int>(j => j.SendAsync(ct))).ShouldBe(1);
        (await Scoped<EventReminderJob, int>(j => j.SendAsync(ct))).ShouldBe(0);
    }

    private Task<Guid> CreateEventAsync(string slug, int? capacity)
    {
        var start = DateTimeOffset.Now.AddDays(10);
        return Scoped<EventService, Guid>(s => s.SaveAsync(null, new EventDetails($"Etkinlik {slug}", slug, null, "Salon A", start, start.AddHours(3), capacity, null, null), ct).Value());
    }

    private Task<Arma.SharedKernel.Domain.Result<CheckInResult>> CheckIn(Guid eventId, string code) =>
        factory.WithScopeAsync(sp => sp.GetRequiredService<EventService>().CheckInAsync(eventId, code, ct));

    private Task<IReadOnlyList<ParticipantView>> Participants(Guid id) => Scoped<EventService, IReadOnlyList<ParticipantView>>(s => s.ParticipantsAsync(id, ct));

    private Task<Guid> Person(string first) =>
        Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest(first, "Katılımcı", $"{first.ToLowerInvariant()}.{Guid.NewGuid():N}@example.org", null, null), ct).Value());

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
