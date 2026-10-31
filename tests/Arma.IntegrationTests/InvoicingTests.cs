using System.Xml.Linq;
using Arma.Modules.Contacts.Application;
using Arma.Modules.Invoicing.Application;
using Arma.Modules.Invoicing.Contracts;
using Arma.Modules.Invoicing.Domain;
using Arma.Modules.Invoicing.Providers;
using Arma.Modules.Membership.Application;
using Arma.Modules.Membership.Domain;
using Arma.Modules.Sales.Application;
using Arma.Modules.Sales.Domain;
using Arma.SharedKernel.Invoicing;
using Microsoft.Extensions.DependencyInjection;

namespace Arma.IntegrationTests;

public sealed class InvoicingTests(ArmaAppFactory factory) : IClassFixture<ArmaAppFactory>
{
    private const string SellerVkn = "8790017566";
    private readonly CancellationToken ct = TestContext.Current.CancellationToken;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.Now);

    [Fact]
    public void Tax_ids_are_checked_with_their_check_digits()
    {
        TaxIds.IsValidVkn(SellerVkn).ShouldBeTrue();
        TaxIds.IsValidVkn("8790017567").ShouldBeFalse();
        TaxIds.IsValidTckn(Tckn()).ShouldBeTrue();
        TaxIds.IsValidTckn("12345678901").ShouldBeFalse();
        TaxIds.IsValid(TaxIds.Anonymous).ShouldBeFalse();
    }

    [Fact]
    public void Builder_lists_every_problem_and_writes_ubl_tr_with_totals()
    {
        var error = Should.Throw<InvalidOperationException>(() => EInvoiceDocumentBuilder.Create("AB1", Guid.Empty).Line("", 0, null, 10m, vatRate: 0).Build());
        error.Message.ShouldContain("fatura numarası");
        error.Message.ShouldContain("ETTN");
        error.Message.ShouldContain("satıcı");
        error.Message.ShouldContain("alıcı adı");
        error.Message.ShouldContain("1. kalem");
        error.Message.ShouldContain("istisna kodu");

        var uuid = Guid.NewGuid();
        var document = EInvoiceDocumentBuilder.Create($"TST{Today.Year}000000042", uuid)
            .IssuedOn(Today, new TimeOnly(14, 30, 5))
            .Seller(Seller())
            .Buyer(new EInvoiceParty("Ayşe Yılmaz", TaxIds.Anonymous) { City = "Ankara" })
            .Line("Danışmanlık", 3, "saat", 333.335m, discountRate: 10, vatRate: 20)
            .Line("Kitap", 2, "adet", 50m, vatRate: 0)
            .Exemption("351", "KDV - İstisna Olmayan Diğer")
            .OrderReference("T2026-0001")
            .Build();

        document.ShouldSatisfyAllConditions(
            d => d.TaxExclusive.ShouldBe(1000.02m),
            d => d.VatTotal.ShouldBe(180.00m),
            d => d.Payable.ShouldBe(1180.02m),
            d => d.TypeCode.ShouldBe("SATIS"),
            d => d.ProfileId.ShouldBe("EARSIVFATURA"));

        var xml = XDocument.Parse(document.Xml);
        string Value(XName name) => xml.Root!.Element(name)!.Value;
        Value(UblTrWriter.Cbc + "ID").ShouldBe($"TST{Today.Year}000000042");
        Value(UblTrWriter.Cbc + "UUID").ShouldBe(uuid.ToString().ToUpperInvariant());
        Value(UblTrWriter.Cbc + "CustomizationID").ShouldBe("TR1.2");
        Value(UblTrWriter.Cbc + "IssueTime").ShouldBe("14:30:05");
        xml.Root!.Element(UblTrWriter.Cac + "LegalMonetaryTotal")!.Element(UblTrWriter.Cbc + "PayableAmount")!.Value.ShouldBe("1180.02");
        xml.Descendants(UblTrWriter.Cac + "InvoiceLine").Count().ShouldBe(2);
        xml.Descendants(UblTrWriter.Cbc + "TaxExemptionReasonCode").Select(e => e.Value).Distinct().ShouldBe(["351"]);
        var customer = xml.Root.Element(UblTrWriter.Cac + "AccountingCustomerParty")!;
        customer.Descendants(UblTrWriter.Cbc + "ID").First().Attribute("schemeID")!.Value.ShouldBe("TCKN");
        customer.Descendants(UblTrWriter.Cbc + "FamilyName").Single().Value.ShouldBe("Yılmaz");
        xml.Descendants(UblTrWriter.Cbc + "InvoicedQuantity").First().Attribute("unitCode")!.Value.ShouldBe("HUR");
    }

    [Fact]
    public async Task Built_in_and_plugin_integrators_are_discovered_and_the_chain_checks_the_profile()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var gateway = scope.ServiceProvider.GetRequiredService<IEInvoiceGateway>();

        gateway.Providers.Select(p => p.Key).ShouldBe([GibPortalFileProvider.Key, TestEInvoiceProvider.Key, "ornek-entegrator"]);
        gateway.Find(GibPortalFileProvider.Key)!.Descriptor.Transmits.ShouldBeFalse();
        // Örnek eklentideki entegratör Arma'nın kodunda adı geçmeden bulunur ve ayarı verilince hazır olur.
        gateway.Find("ornek-entegrator")!.IsConfigured.ShouldBeTrue();

        var eFatura = EInvoiceDocumentBuilder.Create($"TST{Today.Year}000000001", Guid.NewGuid()).Profile(EInvoiceProfiles.EFatura).IssuedOn(Today, default)
            .Seller(Seller()).Buyer(new EInvoiceParty("Alıcı A.Ş.", SellerVkn) { City = "İzmir" }).Line("Hizmet", 1, null, 100m).Build();
        var blocked = await gateway.SendAsync("ornek-entegrator", eFatura, ct);
        blocked.Success.ShouldBeFalse();
        blocked.Error!.ShouldContain("e-Fatura gönderemiyor");
        (await gateway.SendAsync("yok", eFatura, ct)).Error!.ShouldContain("bu kurulumda yok");
        (await gateway.SendAsync(TestEInvoiceProvider.Key, eFatura, ct)).Reference.ShouldBe($"TEST-TST{Today.Year}000000001");
    }

    [Fact]
    public async Task Accepted_quote_and_paid_dues_become_invoices_that_are_numbered_sent_and_cancelled()
    {
        RequiresDatabase.Check();
        await SaveSettingsAsync(TestEInvoiceProvider.Key);
        var tckn = Tckn();
        var customer = await Scoped<ContactService, Guid>(s => s.CreatePersonAsync(new CreatePersonRequest("Fatura", "Alıcı" + Guid.NewGuid().ToString("N")[..6], null, null, tckn), ct).Value());

        // Kabul edilen teklif taslak fatura açar; kalemler ve alıcının kimliği tekliften ve kişiden gelir.
        var quoteId = await Scoped<QuoteService, Guid>(s => s.SaveAsync(null, new QuoteInput(customer, null, Today, Today.AddDays(30), null,
            [new QuoteLineInput("Kurulum", 1, "adet", 4000m, 0, 20), new QuoteLineInput("Eğitim", 2, "gün", 500m, 10, 10)]), ct).Value());
        await Scoped<QuoteService, Guid>(s => s.SetStatusAsync(quoteId, QuoteStatus.Accepted, ct).Value());
        var fromQuote = await EventuallyAsync(() => Find(customer, InvoiceSources.Quote), i => i is not null);
        fromQuote.ShouldNotBeNull();
        fromQuote.ShouldSatisfyAllConditions(
            i => i.Status.ShouldBe(InvoiceStatus.Draft),
            i => i.Number.ShouldBeNull(),
            i => i.BuyerTaxId.ShouldBe(tckn),
            i => i.Total.ShouldBe(5790m));

        // Aynı kaynak için ikinci fatura açılmaz.
        (await Scoped<InvoiceService, Guid?>(s => s.RequestAsync(new InvoiceRequested(InvoiceSources.Quote, quoteId, "x", customer, Today,
            [new InvoiceRequestLine("x", 1, null, 1m, 0, 20)], null), ct))).ShouldBeNull();

        // İl eksikken numara harcanmaz; alıcı düzeltilince fatura numaralanıp gönderilir.
        var invalid = await factory.WithScopeAsync(sp => sp.GetRequiredService<InvoiceService>().IssueAsync(fromQuote.Id, ct));
        invalid.Error!.Message.ShouldContain("alıcının ili");
        (await Get(fromQuote.Id)).Number.ShouldBeNull();
        await Scoped<InvoiceService, Guid>(s => s.UpdateDraftAsync(fromQuote.Id, new InvoiceDraftInput(fromQuote.Buyer with { City = "Ankara" }, EInvoiceProfiles.EArsiv, null), ct).Value());
        await Scoped<InvoiceService, Guid>(s => s.IssueAsync(fromQuote.Id, ct).Value());
        var issued = await Get(fromQuote.Id);
        issued.ShouldSatisfyAllConditions(
            i => i.Status.ShouldBe(InvoiceStatus.Issued),
            i => i.Number!.Length.ShouldBe(16),
            i => i.Number!.ShouldStartWith($"TST{Today.Year}"),
            i => i.ProviderReference.ShouldBe("TEST-" + i.Number),
            i => i.TransmittedAt.ShouldNotBeNull(),
            i => i.Xml!.ShouldContain(i.Number!));
        var provider = factory.Services.GetRequiredService<TestEInvoiceProvider>();
        provider.LastDocument!.OrderReference.ShouldNotBeNull();

        // Alınan aidat KDV dahildir: 600 TL'lik aidat %20 KDV ile 500 + 100 olarak faturalanır.
        var type = await Scoped<MembershipService, Guid>(s => s.SaveTypeAsync(null, new SaveMembershipTypeRequest("Faturalı üyelik " + Guid.NewGuid().ToString("N")[..6], null, 12, 600m, 30, true), ct).Value());
        await Scoped<MembershipService, Guid>(s => s.JoinAsync(new JoinRequest(customer, type, Today, null, new DuesInput(600m, Today, DuesMethod.Cash, null)), ct).Value());
        var fromDues = await EventuallyAsync(() => Find(customer, InvoiceSources.Dues), i => i is not null);
        fromDues.ShouldNotBeNull();
        fromDues.ShouldSatisfyAllConditions(i => i.Subtotal.ShouldBe(500m), i => i.VatTotal.ShouldBe(100m), i => i.Total.ShouldBe(600m));
        await Scoped<InvoiceService, Guid>(s => s.UpdateDraftAsync(fromDues.Id, new InvoiceDraftInput(fromDues.Buyer with { City = "Ankara" }, EInvoiceProfiles.EArsiv, null), ct).Value());

        // Entegratör reddederse numara faturada kalır; yeniden denemede aynı numara kullanılır.
        provider.Reject = true;
        try
        {
            var rejected = await factory.WithScopeAsync(sp => sp.GetRequiredService<InvoiceService>().IssueAsync(fromDues.Id, ct));
            rejected.Error!.Message.ShouldContain("reddetti");
        }
        finally
        {
            provider.Reject = false;
        }

        var failed = await Get(fromDues.Id);
        failed.ShouldSatisfyAllConditions(i => i.Status.ShouldBe(InvoiceStatus.Draft), i => i.Number.ShouldNotBeNull(), i => i.LastError.ShouldNotBeNull());
        await Scoped<InvoiceService, Guid>(s => s.IssueAsync(fromDues.Id, ct).Value());
        (await Get(fromDues.Id)).ShouldSatisfyAllConditions(i => i.Number.ShouldBe(failed.Number), i => i.LastError.ShouldBeNull());
        long.Parse(failed.Number![7..]).ShouldBeGreaterThan(long.Parse(issued.Number![7..]));

        // Numaralı fatura silinmez, nedeniyle iptal edilir.
        var noReason = await factory.WithScopeAsync(sp => sp.GetRequiredService<InvoiceService>().CancelAsync(fromDues.Id, null, ct));
        noReason.IsSuccess.ShouldBeFalse();
        (await Scoped<InvoiceService, bool>(s => s.CancelAsync(fromDues.Id, "Yanlış tutar", ct).Value())).ShouldBeFalse();
        (await Get(fromDues.Id)).Status.ShouldBe(InvoiceStatus.Cancelled);
    }

    [Fact]
    public async Task Portal_file_waits_for_upload_and_plugin_integrator_sends()
    {
        RequiresDatabase.Check();
        var customer = await Scoped<ContactService, Guid>(s => s.CreateOrganizationAsync(new CreateOrganizationRequest("Fatura Kurumu " + Guid.NewGuid().ToString("N")[..6], null, null, null), ct).Value());

        await SaveSettingsAsync(GibPortalFileProvider.Key);
        var portal = await Manual(customer);
        await Scoped<InvoiceService, Guid>(s => s.IssueAsync(portal, ct).Value());
        var prepared = await Get(portal);
        prepared.ShouldSatisfyAllConditions(i => i.Status.ShouldBe(InvoiceStatus.Issued), i => i.TransmittedAt.ShouldBeNull(), i => i.Provider.ShouldBe(GibPortalFileProvider.Key));
        (await Scoped<InvoiceService, IReadOnlyList<InvoiceListItem>>(s => s.ListAsync(new InvoiceFilter(PendingTransmission: true), ct))).ShouldContain(i => i.Id == portal);
        var file = (await Scoped<InvoiceService, (string FileName, string Xml)?>(s => s.XmlAsync(portal, ct)))!.Value;
        file.FileName.ShouldBe($"{prepared.Number}.xml");
        await Scoped<InvoiceService, Guid>(s => s.MarkTransmittedAsync(portal, ct).Value());
        (await Get(portal)).TransmittedAt.ShouldNotBeNull();

        await SaveSettingsAsync("ornek-entegrator");
        var plugin = await Manual(customer);
        await Scoped<InvoiceService, Guid>(s => s.IssueAsync(plugin, ct).Value());
        (await Get(plugin)).ProviderReference!.ShouldStartWith("ORNEK-");

        // Numarası olmayan taslak silinir.
        var draft = await Manual(customer);
        (await Scoped<InvoiceService, bool>(s => s.CancelAsync(draft, null, ct).Value())).ShouldBeTrue();
        (await Scoped<InvoiceService, Invoice?>(s => s.GetAsync(draft, ct))).ShouldBeNull();
        await SaveSettingsAsync(TestEInvoiceProvider.Key);
    }

    private async Task<Guid> Manual(Guid customer)
    {
        Guid id = (Guid)(await Scoped<InvoiceService, Guid?>(s => s.RequestAsync(new InvoiceRequested(InvoiceService.ManualSource, Guid.NewGuid(), null, customer, Today,
            [new InvoiceRequestLine("Danışmanlık", 1, "saat", 1000m, 0, 20)], null), ct))).ShouldNotBeNull();
        var invoice = await Get(id);
        await Scoped<InvoiceService, Guid>(s => s.UpdateDraftAsync(id, new InvoiceDraftInput(invoice.Buyer with { TaxId = SellerVkn, City = "İzmir", TaxOffice = "Konak" },
            EInvoiceProfiles.EArsiv, null), ct).Value());
        return id;
    }

    private Task SaveSettingsAsync(string provider) =>
        Scoped<InvoiceService, bool>(s => s.SaveSettingsAsync(new InvoiceSettings
        {
            Prefix = "TST",
            SellerName = "Arma Test Derneği",
            SellerTaxId = SellerVkn,
            SellerTaxOffice = "Çankaya",
            SellerCity = "Ankara",
            Provider = provider,
            FromQuotes = true,
            FromDues = true,
            DuesVatRate = 20,
        }, ct).Value());

    private async Task<Invoice?> Find(Guid contactId, string source)
    {
        var items = await Scoped<InvoiceService, IReadOnlyList<InvoiceListItem>>(s => s.ListAsync(new InvoiceFilter(ContactId: contactId), ct));
        return items.FirstOrDefault(i => i.SourceKind == source) is { } item ? await Get(item.Id) : null;
    }

    private async Task<Invoice> Get(Guid id) => (await Scoped<InvoiceService, Invoice?>(s => s.GetAsync(id, ct)))!;

    private static EInvoiceParty Seller() => new("Arma Test Derneği", SellerVkn) { TaxOffice = "Çankaya", City = "Ankara" };

    /// <summary>Kontrol haneleri doğru, rastgele T.C. kimlik numarası.</summary>
    private static string Tckn()
    {
        var d = new int[11];
        d[0] = Random.Shared.Next(1, 10);
        for (var i = 1; i < 9; i++)
        {
            d[i] = Random.Shared.Next(0, 10);
        }

        d[9] = (((d[0] + d[2] + d[4] + d[6] + d[8]) * 7 - (d[1] + d[3] + d[5] + d[7])) % 10 + 10) % 10;
        d[10] = d.Take(10).Sum() % 10;
        return string.Concat(d);
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
