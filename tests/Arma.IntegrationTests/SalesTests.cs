using Arma.Modules.Activities.Application;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Sales.Application;
using Arma.Modules.Sales.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class SalesTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    [Fact]
    public void Line_amounts_apply_discount_then_vat_with_kurus_rounding()
    {
        var line = new QuoteLineInput("Danışmanlık", 3, "saat", 333.335m, 10, 20);
        QuoteLine.NetOf(line).ShouldBe(900.00m);
        QuoteLine.VatOf(line).ShouldBe(180.00m);
        QuoteLine.NetOf(new QuoteLineInput("Kalem", 1, null, 0.125m, 0, 0)).ShouldBe(0.13m);
    }

    [Fact]
    public async Task Board_moves_reorder_close_and_protect_stages_in_use()
    {
        RequiresDatabase.Check();
        var pipeline = (await Scoped<SalesService, IReadOnlyList<PipelineView>>(s => s.PipelinesAsync(ct))).Single(p => p.IsDefault);
        var first = pipeline.Stages[0];
        var won = pipeline.Stages.Single(s => s.Kind == StageKind.Won);
        var lost = pipeline.Stages.Single(s => s.Kind == StageKind.Lost);
        var customer = await Person("Pano", "Müşteri");

        var a = await Create("Pano A", customer, 1000m);
        var b = await Create("Pano B", customer, 2000m);
        var c = await Create("Pano C", customer, 3000m);
        await Scoped<SalesService, Guid>(s => s.MoveAsync(c, first.Id, a, cancellationToken: ct).Value());
        (await Column(first.Id, "Pano")).ShouldBe(["Pano C", "Pano A", "Pano B"]);

        await Scoped<SalesService, Guid>(s => s.MoveAsync(a, won.Id, cancellationToken: ct).Value());
        await Scoped<SalesService, Guid>(s => s.MoveAsync(b, lost.Id, lostReason: "Bütçe yok", cancellationToken: ct).Value());
        var closedA = (await Get(a))!;
        closedA.ShouldSatisfyAllConditions(o => o.ClosedOn.ShouldBe(Today), o => o.Probability.ShouldBe(100));
        (await Get(b))!.LostReason.ShouldBe("Bütçe yok");

        // Açık aşamaya dönen fırsatın kapanış bilgisi silinir.
        await Scoped<SalesService, Guid>(s => s.MoveAsync(b, first.Id, cancellationToken: ct).Value());
        (await Get(b))!.ShouldSatisfyAllConditions(o => o.ClosedOn.ShouldBeNull(), o => o.LostReason.ShouldBeNull());

        var without = pipeline.Stages.Where(s => s.Id != first.Id).Select(s => new StageInput(s.Id, s.Name, s.Probability, s.Kind)).ToList();
        var removed = await factory.WithScopeAsync(sp => sp.GetRequiredService<SalesService>().SavePipelineAsync(pipeline.Id, pipeline.Name, without, ct));
        removed.Error.ShouldBe(SalesErrors.StageInUse);

        var timeline = await EventuallyAsync(
            () => factory.WithScopeAsync(sp => sp.GetRequiredService<ActivityService>().TimelineAsync(customer, cancellationToken: ct)),
            t => t.History.Any(e => e.Subject.Contains("kaybedildi")));
        timeline.History.ShouldContain(e => e.Subject.Contains("Pano A") && e.Subject.Contains("kazanıldı"));
    }

    [Fact]
    public async Task Quotes_number_total_and_close_the_linked_opportunity()
    {
        RequiresDatabase.Check();
        var customer = await Person("Teklif", "Müşteri");
        var opportunity = await Create("Teklifli iş", customer, 5000m);
        var input = new QuoteInput(customer, opportunity, Today, Today.AddDays(30), "Peşin ödeme",
            [new QuoteLineInput("Kurulum", 1, "adet", 4000m, 0, 20), new QuoteLineInput("Eğitim", 2, "gün", 500m, 10, 10)]);

        var first = await Scoped<QuoteService, Guid>(s => s.SaveAsync(null, input, ct).Value());
        var second = await Scoped<QuoteService, Guid>(s => s.SaveAsync(null, input with { OpportunityId = null }, ct).Value());
        var quote = (await Scoped<QuoteService, QuoteView?>(s => s.GetAsync(first, ct)))!;
        quote.ShouldSatisfyAllConditions(
            q => q.Number.ShouldStartWith($"T{Today.Year}-"),
            q => q.Subtotal.ShouldBe(4900m),
            q => q.VatTotal.ShouldBe(890m),
            q => q.Total.ShouldBe(5790m),
            q => q.Lines.Select(l => l.Position).ShouldBe([1, 2]));
        var next = (await Scoped<QuoteService, QuoteView?>(s => s.GetAsync(second, ct)))!.Number;
        int.Parse(next[^4..]).ShouldBe(int.Parse(quote.Number[^4..]) + 1);

        await Scoped<QuoteService, Guid>(s => s.SetStatusAsync(first, QuoteStatus.Sent, ct).Value());
        var deleteSent = await factory.WithScopeAsync(sp => sp.GetRequiredService<QuoteService>().DeleteAsync(first, ct));
        deleteSent.Error.ShouldBe(QuoteErrors.OnlyDraftDeletable);
        await Scoped<QuoteService, Guid>(s => s.SetStatusAsync(first, QuoteStatus.Accepted, ct).Value());
        (await Get(opportunity))!.StageKind.ShouldBe(StageKind.Won);

        await Scoped<QuoteService, Guid>(s => s.DeleteAsync(second, ct).Value());
        (await Scoped<QuoteService, QuoteView?>(s => s.GetAsync(second, ct))).ShouldBeNull();

        var criteria = factory.Services.CreateScope().ServiceProvider.GetServices<IContactCriteriaProvider>().OfType<SalesInsights>().Single();
        (await criteria.MatchAsync("sales_won_since", Today.ToString("yyyy-MM-dd"), ct))!.ContactIds.ShouldContain(customer);
    }

    private Task<Guid> Create(string title, Guid contact, decimal amount) =>
        Scoped<SalesService, Guid>(s => s.CreateAsync(new OpportunityInput(title, contact, amount, Today.AddDays(14), null, null), cancellationToken: ct).Value());

    private async Task<IReadOnlyList<string>> Column(Guid stageId, string search)
    {
        var board = await Scoped<SalesService, Board>(s => s.BoardAsync(null, new BoardFilter(Search: search), ct));
        return [.. board.Columns.Single(c => c.Stage.Id == stageId).Items.Select(o => o.Title)];
    }

    private Task<OpportunityView?> Get(Guid id) => Scoped<SalesService, OpportunityView?>(s => s.GetAsync(id, ct));

    private Task<Guid> Person(string first, string last) =>
        Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest(first, last, $"{first.ToLowerInvariant()}.{last.ToLowerInvariant()}@example.org", null, null), ct).Value());

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
