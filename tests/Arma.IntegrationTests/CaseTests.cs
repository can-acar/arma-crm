using Arma.Modules.Activities.Application;
using Arma.Modules.Cases.Application;
using Arma.Modules.Cases.Domain;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Identity.Application;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Privacy;
using Case = Arma.Modules.Cases.Domain.Case;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class CaseTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    private DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    [Fact]
    public void Case_type_validates_steps_and_closing_requires_an_outcome()
    {
        CaseType.Create(" ", null, []).IsSuccess.ShouldBeFalse();
        CaseType.Create("Hukuki destek", null, [new StepTemplate("Ön görüşme", -1)]).IsSuccess.ShouldBeFalse();
        var type = CaseType.Create("Hukuki destek", null, [new StepTemplate("Ön görüşme", 0), new StepTemplate(" ", 3), new StepTemplate("Dilekçe", 7)]).Value!;
        type.Steps.Select(s => s.Title).ShouldBe(["Ön görüşme", "Dilekçe"]);

        var opened = Case.Open("V2026-0001", type, Guid.NewGuid(), "Kira davası", null, CasePriority.High, null, new DateOnly(2026, 3, 1)).Value!;
        opened.Steps.Select(s => s.DueOn).ShouldBe([new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 8)]);
        opened.ChangeStatus(CaseStatus.Closed, " ", new DateOnly(2026, 3, 9)).IsSuccess.ShouldBeFalse();
        opened.ChangeStatus(CaseStatus.Closed, "Uzlaşma sağlandı", new DateOnly(2026, 3, 9)).IsSuccess.ShouldBeTrue();
        (opened.ClosedOn, opened.Outcome).ShouldBe((new DateOnly(2026, 3, 9), "Uzlaşma sağlandı"));
        opened.ChangeStatus(CaseStatus.Open, null, new DateOnly(2026, 3, 10)).IsSuccess.ShouldBeTrue();
        (opened.ClosedOn, opened.Outcome).ShouldBe(((DateOnly?)null, (string?)null));
    }

    [Fact]
    public async Task Case_is_opened_from_its_type_steps_are_tracked_and_confidential_notes_stay_off_the_timeline()
    {
        RequiresDatabase.Check();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var type = await Scoped<CaseService, Guid>(s => s.SaveTypeAsync(null, new SaveCaseTypeRequest("Sosyal yardım " + suffix, null, true,
            [new StepTemplate("İlk görüşme", 0), new StepTemplate("Ev ziyareti", 3), new StepTemplate("Kurul kararı", 14)]), ct).Value());
        (await Scoped<CaseService, Result<Guid>>(s => s.SaveTypeAsync(null, new SaveCaseTypeRequest("sosyal YARDIM " + suffix, null, true, []), ct)))
            .Error.ShouldBe(CaseErrors.DuplicateType);

        var contact = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Vaka", "Sahibi" + suffix[..3], null, null, null), ct).Value());
        var userId = await Scoped<UserAdminService, Guid>(s => s.CreateAsync(new CreateUserRequest("Sosyal Hizmet " + suffix, $"sh{suffix}@example.org",
            "Aa1!" + Guid.NewGuid().ToString("N"), Arma.Modules.Identity.Domain.SystemRoles.Staff), ct).Value());

        var id = await Scoped<CaseService, Guid>(s => s.OpenAsync(new OpenCaseRequest(contact, type, "Kira yardımı", "Üç aylık kira borcu", CasePriority.High, userId), ct).Value());
        (await Scoped<CaseService, Result<Guid>>(s => s.OpenAsync(new OpenCaseRequest(contact, type, "Kira", null, CasePriority.Normal, Guid.NewGuid()), ct)))
            .Error!.Code.ShouldBe("cases.assignee");

        var detail = (await Scoped<CaseService, CaseDetail?>(s => s.GetAsync(id, ct)))!;
        detail.Summary.Number.ShouldStartWith($"V{Today.Year}-");
        (detail.Summary.AssigneeName, detail.Summary.StepsTotal, detail.Summary.NextDueOn).ShouldBe(("Sosyal Hizmet " + suffix, 3, Today));
        detail.Steps.Select(s => s.DueOn).ShouldBe([Today, Today.AddDays(3), Today.AddDays(14)]);

        await Scoped<CaseService, Guid>(s => s.SetStepDoneAsync(detail.Steps[0].Id, true, ct).Value());
        var added = await Scoped<CaseService, Guid>(s => s.AddStepAsync(id, "Belge toplama", Today.AddDays(-1), ct).Value());
        detail = (await Scoped<CaseService, CaseDetail?>(s => s.GetAsync(id, ct)))!;
        (detail.Summary.StepsDone, detail.Summary.StepsTotal, detail.Summary.IsOverdue).ShouldBe((1, 4, true));
        detail.Steps.Single(s => s.Id == added).IsOverdue.ShouldBeTrue();

        var mine = await Scoped<CaseService, CasePage>(s => s.ListAsync(new CaseFilter(AssigneeId: userId, OverdueOnly: true), ct));
        mine.Items.Single().Id.ShouldBe(id);
        (await Scoped<CaseService, CaseSummary>(s => s.SummaryAsync(userId, ct))).ShouldBe(new CaseSummary(
            (await Scoped<CaseService, CaseSummary>(s => s.SummaryAsync(null, ct))).Open,
            (await Scoped<CaseService, CaseSummary>(s => s.SummaryAsync(null, ct))).Overdue, 1, 1));
        (await Scoped<CaseService, CasePage>(s => s.ListAsync(new CaseFilter(Search: "kira yard"), ct))).Items.ShouldContain(c => c.Id == id);

        await Scoped<CaseService, Guid>(s => s.AddNoteAsync(id, "Ev ziyareti yapıldı.", false, ct).Value());
        await Scoped<CaseService, Guid>(s => s.AddNoteAsync(id, "Sağlık durumu: kronik hastalık.", true, ct).Value());
        (await Scoped<CaseService, IReadOnlyList<CaseNoteView>>(s => s.NotesAsync(id, false, ct))).Single().Body.ShouldBe("Ev ziyareti yapıldı.");
        (await Scoped<CaseService, IReadOnlyList<CaseNoteView>>(s => s.NotesAsync(id, true, ct))).Count.ShouldBe(2);

        (await Scoped<CaseService, Result<Guid>>(s => s.ChangeStatusAsync(id, CaseStatus.Closed, null, ct))).IsSuccess.ShouldBeFalse();
        await Scoped<CaseService, Guid>(s => s.ChangeStatusAsync(id, CaseStatus.Closed, "Üç aylık kira ödendi", ct).Value());
        (await Scoped<CaseService, Result<Guid>>(s => s.AddStepAsync(id, "Geç adım", null, ct))).Error.ShouldBe(CaseErrors.Closed);

        var timeline = await EventuallyAsync(
            () => factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>().TimelineAsync(contact, cancellationToken: ct)),
            t => t.History.Any(a => a.Subject.StartsWith("Vaka kapandı")));
        timeline.History.ShouldContain(a => a.Subject.StartsWith("Vaka açıldı"));
        timeline.History.ShouldContain(a => a.Subject.StartsWith("Vaka notu") && a.Body == "Ev ziyareti yapıldı.");
        timeline.History.ShouldNotContain(a => a.Body != null && a.Body.Contains("kronik"));

        var criteria = factory.Services.CreateScope().ServiceProvider.GetServices<IContactCriteriaProvider>().OfType<CasesInsights>().Single();
        (await criteria.MatchAsync("case_type", type.ToString(), ct))!.ContactIds.ShouldBe([contact]);
        (await criteria.MatchAsync("case_open", null, ct))!.ContactIds.ShouldNotContain(contact);
    }

    [Fact]
    public async Task Merged_contact_brings_cases_and_anonymization_clears_their_text()
    {
        RequiresDatabase.Check();
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var type = await Scoped<CaseService, Guid>(s => s.SaveTypeAsync(null, new SaveCaseTypeRequest("Şikâyet " + suffix, null, true, []), ct).Value());
        var primary = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Asıl", "Kayıt" + suffix[..3], null, null, null), ct).Value());
        var duplicate = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Asıl", "Kayıt" + suffix[..3], null, null, null), ct).Value());
        var id = await Scoped<CaseService, Guid>(s => s.OpenAsync(new OpenCaseRequest(duplicate, type, "Gürültü şikâyeti", "Komşu", CasePriority.Low, null), ct).Value());
        await Scoped<CaseService, Guid>(s => s.AddNoteAsync(id, "Gizli ayrıntı", true, ct).Value());

        await Scoped<ContactService, Guid>(s => s.MergeAsync(primary, duplicate, ct).Value());
        var moved = await EventuallyAsync(() => Scoped<CaseService, IReadOnlyList<CaseListItem>>(s => s.ForContactAsync(primary, ct)), l => l.Count == 1);
        moved.Single().Id.ShouldBe(id);

        var source = factory.Services.CreateScope().ServiceProvider.GetServices<IPersonalDataSource>().OfType<CasesPersonalData>().Single();
        var export = await source.ExportAsync(primary, ct);
        export.Single(s => s.Title == "Vaka notları").Rows.Single()["Not"].ShouldBe("Gizli ayrıntı");

        await factory.WithScopeAsync(async sp =>
        {
            await sp.GetServices<IPersonalDataSource>().OfType<CasesPersonalData>().Single().AnonymizeAsync(primary, ct);
            return true;
        });
        var anonymized = (await Scoped<CaseService, CaseDetail?>(s => s.GetAsync(id, ct)))!;
        (anonymized.Summary.Subject, anonymized.Description).ShouldBe(("Anonimleştirilmiş vaka", (string?)null));
        (await Scoped<CaseService, IReadOnlyList<CaseNoteView>>(s => s.NotesAsync(id, true, ct))).Single().Body.ShouldBe("[anonimleştirildi]");
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
