using System.Globalization;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Membership.Domain;
using Arma.Modules.Membership.Infrastructure;
using Arma.Modules.Membership.Web;
using Arma.SharedKernel.Modules;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Membership.Application;

/// <summary>Gelişmiş aramaya üyelik ölçütleri, panoya üye sayıları.</summary>
internal sealed class MembershipInsights(MembershipDbContext db, MembershipService members) : IContactCriteriaProvider, IDashboardContributor
{
    private const string Group = "Üyelik";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public async Task<IReadOnlyList<CriterionDefinition>> DefinitionsAsync(CancellationToken cancellationToken)
    {
        var types = await db.Types.AsNoTracking().OrderBy(t => t.Name).Select(t => new CriterionOption(t.Id.ToString(), t.Name)).ToListAsync(cancellationToken);
        CriterionOption[] statuses = [.. MembershipLabels.Statuses.Select(s => new CriterionOption(s.ToString(), MembershipLabels.Status(s)))];
        return
        [
            new("member_status", Group, "Üyelik durumu", CriterionKind.Choice, statuses),
            new("member_type", Group, "Bu türde üyeliği var", CriterionKind.Choice, types),
            new("member_ends_before", Group, "Üyeliği bu tarihten önce bitiyor", CriterionKind.Date),
            new("member_never", Group, "Hiç üye olmadı", CriterionKind.Flag),
        ];
    }

    public async Task<CriterionMatch?> MatchAsync(string key, string? value, CancellationToken cancellationToken)
    {
        switch (key)
        {
            case "member_status" when Enum.TryParse<MembershipStatus>(value, out var status):
                return new(await Contacts(new MemberFilter(Status: status), cancellationToken));
            case "member_type" when Guid.TryParse(value, out var type):
                return new(await Contacts(new MemberFilter(TypeId: type), cancellationToken));
            case "member_ends_before" when DateOnly.TryParse(value, CultureInfo.InvariantCulture, out var date):
                return new(await Contacts(new MemberFilter(Status: MembershipStatus.Current, EndsBefore: date), cancellationToken));
            case "member_never":
                return new(await db.Members.Select(m => m.ContactId).Distinct().ToListAsync(cancellationToken), Exclude: true);
            default:
                return null;
        }
    }

    public async Task<IReadOnlyList<DashboardTile>> TilesAsync(CancellationToken cancellationToken)
    {
        var current = await members.Query(new MemberFilter(Status: MembershipStatus.Current)).Select(x => x.Member.ContactId).Distinct().CountAsync(cancellationToken);
        var grace = await members.Query(new MemberFilter(Status: MembershipStatus.Grace)).CountAsync(cancellationToken);
        var soon = await members.Query(new MemberFilter(Status: MembershipStatus.Current, EndsBefore: members.Today.AddDays(30))).CountAsync(cancellationToken);
        return
        [
            new("Aktif üye", current.ToString("N0", Turkish), grace > 0 ? $"{grace} üyelik aidat bekliyor" : "Aidat bekleyen yok", "memberships?status=Current", 40, MembershipPermissions.Read),
            new("30 günde yenilenecek", soon.ToString("N0", Turkish), "Süresi dolmak üzere olan üyelik", "memberships?status=Current&soon=1", 41, MembershipPermissions.Read),
        ];
    }

    private Task<List<Guid>> Contacts(MemberFilter filter, CancellationToken cancellationToken) =>
        members.Query(filter).Select(x => x.Member.ContactId).Distinct().ToListAsync(cancellationToken);
}
