using Arma.Modules.Giving.Domain;
using Arma.Modules.Giving.Infrastructure;
using Arma.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Giving.Application;

public sealed record FundView(Guid Id, string Name, string? Description, bool IsActive, bool IsPublic, decimal TotalTry, int DonationCount);

public sealed record SaveFundRequest(string Name, string? Description, bool IsActive, bool IsPublic);

public sealed class FundService(GivingDbContext db)
{
    public async Task<IReadOnlyList<FundView>> ListAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var funds = db.Funds.AsNoTracking();
        if (activeOnly)
        {
            funds = funds.Where(f => f.IsActive);
        }

        return await funds.OrderBy(f => f.Name)
            .Select(f => new FundView(f.Id, f.Name, f.Description, f.IsActive, f.IsPublic,
                db.Donations.Where(d => d.FundId == f.Id && d.Status == DonationStatus.Completed && d.Currency == Donation.DefaultCurrency).Sum(d => (decimal?)d.Amount) ?? 0,
                db.Donations.Count(d => d.FundId == f.Id && d.Status == DonationStatus.Completed)))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Çevrim içi bağış formunda gösterilen fonlar.</summary>
    public async Task<IReadOnlyList<FundView>> PublicAsync(CancellationToken cancellationToken = default) =>
        (await ListAsync(true, cancellationToken)).Where(f => f.IsPublic).ToList();

    public async Task<Result<Guid>> CreateAsync(SaveFundRequest request, CancellationToken cancellationToken = default)
    {
        if (await NameTakenAsync(request.Name, null, cancellationToken))
        {
            return Duplicate;
        }

        var created = Fund.Create(request.Name, request.Description, request.IsPublic);
        if (!created.IsSuccess)
        {
            return created.Error!;
        }

        db.Funds.Add(created.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return created.Value!.Id;
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, SaveFundRequest request, CancellationToken cancellationToken = default)
    {
        var fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (fund is null)
        {
            return DonationErrors.FundNotFound;
        }

        if (await NameTakenAsync(request.Name, id, cancellationToken))
        {
            return Duplicate;
        }

        var updated = fund.Update(request.Name, request.Description, request.IsActive, request.IsPublic);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    private static readonly Error Duplicate = new("funds.duplicate", "Bu adla bir fon zaten var.");

    private Task<bool> NameTakenAsync(string name, Guid? except, CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim() ?? "";
        return db.Funds.AnyAsync(f => f.Name == trimmed && f.Id != except, cancellationToken);
    }
}
