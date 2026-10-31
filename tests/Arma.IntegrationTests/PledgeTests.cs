using System.Text;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Giving.Application;
using Arma.Modules.Giving.Application.Banking;
using Arma.Modules.Giving.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class PledgeTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public void Schedule_splits_amount_evenly_with_remainder_on_last_installment_and_applies_payments_in_order()
    {
        var pledge = Pledge.Create(Guid.NewGuid(), 1000m, null, null, PledgeFrequency.Quarterly, 3, new DateOnly(2026, 1, 31), null).Value!;
        var schedule = pledge.Schedule(400m);
        schedule.Select(i => i.Amount).ShouldBe([333.33m, 333.33m, 333.34m]);
        schedule.Select(i => i.DueOn).ShouldBe([new DateOnly(2026, 1, 31), new DateOnly(2026, 4, 30), new DateOnly(2026, 7, 31)]);
        schedule.Select(i => i.Paid).ShouldBe([333.33m, 66.67m, 0m]);
        pledge.DueBy(new DateOnly(2026, 5, 1)).ShouldBe(666.66m);

        var once = Pledge.Create(Guid.NewGuid(), 50m, "usd", null, PledgeFrequency.Once, 12, new DateOnly(2026, 6, 1), null).Value!;
        (once.InstallmentCount, once.Currency).ShouldBe((1, "USD"));
        Pledge.Create(Guid.NewGuid(), 0.05m, null, null, PledgeFrequency.Monthly, 12, new DateOnly(2026, 6, 1), null).Error!.Code.ShouldBe("pledges.installments");
    }

    [Fact]
    public async Task Linked_donations_pay_installments_refunds_reopen_them_and_overdue_follows_the_schedule()
    {
        RequiresDatabase.Check();
        var donor = await Person("Taahhüt", "Veren");
        var other = await Person("Başka", "Bağışçı");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var id = await Scoped<PledgeService, Guid>(s => s.CreateAsync(new SavePledgeRequest(donor, 1200m, "TRY", null, PledgeFrequency.Monthly, 12, today.AddMonths(-2), "Burs"), ct).Value());

        var pledge = (await Scoped<PledgeService, PledgeDetail?>(s => s.GetAsync(id, ct)))!.Pledge;
        (pledge.State, pledge.Overdue, pledge.NextDueOn).ShouldBe((PledgeState.Overdue, 300m, today.AddMonths(-2)));

        var payment = await Donate(donor, 250m, "TRY", id);
        (await Donate(donor, 50m, "TRY", id)).ShouldNotBe(Guid.Empty);
        pledge = (await Scoped<PledgeService, PledgeDetail?>(s => s.GetAsync(id, ct)))!.Pledge;
        (pledge.Paid, pledge.Overdue, pledge.State, pledge.NextDueOn).ShouldBe((300m, 0m, PledgeState.Open, today.AddMonths(1)));
        (await Scoped<PledgeService, IReadOnlyList<PledgeView>>(s => s.ListAsync(PledgeState.Overdue, donor, cancellationToken: ct))).ShouldBeEmpty();

        // Başka kişinin, başka para biriminin bağışı bağlanamaz.
        (await Record(other, 10m, "TRY", id)).Error!.Code.ShouldBe("pledges.contact");
        (await Record(donor, 10m, "EUR", id)).Error!.Code.ShouldBe("pledges.currency_mismatch");

        await Scoped<DonationService, Guid>(s => s.RefundAsync(payment, "Hatalı çekim", ct).Value());
        pledge = (await Scoped<PledgeService, PledgeDetail?>(s => s.GetAsync(id, ct)))!.Pledge;
        (pledge.Paid, pledge.Overdue).ShouldBe((50m, 250m));

        (await factory.WithScopeAsync(sp => sp.GetRequiredService<PledgeService>().DeleteAsync(id, ct))).Error!.Code.ShouldBe("pledges.has_payments");
        await Scoped<PledgeService, Guid>(s => s.CancelAsync(id, "Bağışçı vazgeçti", ct).Value());
        (await Record(donor, 10m, "TRY", id)).Error!.Code.ShouldBe("pledges.not_active");
        pledge = (await Scoped<PledgeService, PledgeDetail?>(s => s.GetAsync(id, ct)))!.Pledge;
        (pledge.State, pledge.CancelReason).ShouldBe((PledgeState.Cancelled, "Bağışçı vazgeçti"));
    }

    [Fact]
    public async Task Bank_transfer_is_linked_to_the_open_pledge_and_merge_moves_pledges()
    {
        RequiresDatabase.Check();
        var suffix = new string(Guid.NewGuid().ToString("N").Where(char.IsLetter).Take(4).ToArray()).PadRight(4, 'a').ToUpperInvariant();
        var last = "Taksitçi" + suffix;
        var donor = await Person("Murat", last);
        var fund = await Scoped<FundService, Guid>(s => s.CreateAsync(new SaveFundRequest("Taahhüt fonu " + suffix, null, true, false), ct).Value());
        var today = DateOnly.FromDateTime(DateTime.Today);
        var id = await Scoped<PledgeService, Guid>(s => s.CreateAsync(new SavePledgeRequest(donor, 600m, null, fund, PledgeFrequency.Monthly, 6, today, null), ct).Value());

        var csv = $"Tarih;Açıklama;Tutar\n{today:dd.MM.yyyy};FAST - MURAT {last.ToUpperInvariant()} - aylık;100,00\n";
        var summary = await Scoped<BankService, ImportSummary>(s => s.ImportAsync($"taahhut-{suffix}.csv", Encoding.UTF8.GetBytes(csv), ct).Value());
        (await Scoped<BankService, int>(s => s.ConfirmSuggestedAsync(summary.StatementId, ct))).ShouldBe(1);
        var detail = (await Scoped<PledgeService, PledgeDetail?>(s => s.GetAsync(id, ct)))!;
        detail.Payments.Single().FundId.ShouldBe(fund);
        detail.Pledge.Paid.ShouldBe(100m);

        var duplicate = await Person("Murat", last + "x");
        var second = await Scoped<PledgeService, Guid>(s => s.CreateAsync(new SavePledgeRequest(duplicate, 100m, null, null, PledgeFrequency.Once, 1, today, null), ct).Value());
        await Scoped<ContactService, Guid>(s => s.MergeAsync(donor, duplicate, ct).Value());
        var moved = await EventuallyAsync(() => Scoped<PledgeService, IReadOnlyList<PledgeView>>(s => s.ListAsync(contactId: donor, cancellationToken: ct)), l => l.Count == 2);
        moved.Select(p => p.Id).ShouldBe([id, second], ignoreOrder: true);
    }

    private Task<Guid> Person(string first, string last) =>
        Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest(first, last, null, null, null), ct).Value());

    private Task<Arma.SharedKernel.Domain.Result<Guid>> Record(Guid contact, decimal amount, string currency, Guid pledge) =>
        factory.WithScopeAsync(sp => sp.GetRequiredService<DonationService>().RecordAsync(
            new SaveDonationRequest(contact, amount, currency, DateOnly.FromDateTime(DateTime.Today), null, PaymentMethod.Cash, null, null, null, pledge), ct));

    private Task<Guid> Donate(Guid contact, decimal amount, string currency, Guid pledge) => Record(contact, amount, currency, pledge).Value();

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
