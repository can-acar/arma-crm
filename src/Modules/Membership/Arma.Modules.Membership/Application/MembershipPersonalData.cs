using Arma.Modules.Membership.Infrastructure;
using Arma.Modules.Membership.Web;
using Arma.SharedKernel.Privacy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Membership.Application;

internal sealed class MembershipPersonalData(MembershipDbContext db, MembershipService members) : IPersonalDataSource
{
    public int Order => 250;

    public IReadOnlyList<PersonalDataItem> Inventory { get; } =
    [
        new("Üyelik", "Üye numarası, üyelik türü, giriş ve dönem tarihleri, ayrılış nedeni", "Üyelik", "Üye kaydı ve aidat takibi", "Kanuni yükümlülük (5253 sayılı Dernekler Kanunu, üye kayıt defteri)", "Üyelik süresince ve ayrılıştan sonra 10 yıl"),
        new("Üyelik", "Aidat ödemeleri", "Finans", "Aidat tahsilatı ve muhasebe", "Kanuni yükümlülük (VUK)", "10 yıl"),
    ];

    public async Task<IReadOnlyList<PersonalDataSection>> ExportAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var memberships = await members.ForContactAsync(contactId, cancellationToken);
        var ids = memberships.Select(m => m.Id).ToList();
        var payments = await db.Payments.AsNoTracking().Where(p => ids.Contains(p.MemberId)).OrderBy(p => p.PaidOn).ToListAsync(cancellationToken);
        return
        [
            new("Üyelikler", [.. memberships.Select(m => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Üye no"] = m.Number.ToString(),
                ["Tür"] = m.TypeName,
                ["Giriş"] = MembershipLabels.Date(m.JoinedOn),
                ["Dönem"] = MembershipService.PeriodText(m.StartOn, m.EndOn),
                ["Durum"] = MembershipLabels.Status(m.Status),
                ["Ayrılış nedeni"] = m.CancelReason,
                ["Not"] = m.Note,
            })]),
            new("Aidat ödemeleri", [.. payments.Select(p => (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["Tarih"] = MembershipLabels.Date(p.PaidOn),
                ["Tutar"] = MembershipLabels.Money(p.Amount),
                ["Yöntem"] = MembershipLabels.Method(p.Method),
                ["Not"] = p.Note,
            })]),
        ];
    }

    /// <summary>Üye kaydı ve aidatlar kanunen saklanır; serbest metin notları ve ayrılış nedeni silinir.</summary>
    public async Task AnonymizeAsync(Guid contactId, CancellationToken cancellationToken)
    {
        var ids = await db.Members.Where(m => m.ContactId == contactId).Select(m => m.Id).ToListAsync(cancellationToken);
        await db.Members.Where(m => m.ContactId == contactId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Note, (string?)null).SetProperty(m => m.CancelReason, (string?)null), cancellationToken);
        await db.Payments.Where(p => ids.Contains(p.MemberId))
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Note, (string?)null), cancellationToken);
    }
}
