using Arma.SharedKernel.Domain;

namespace Arma.Modules.Campaigns.Domain;

public enum CampaignStatus
{
    Planned = 1,
    Active = 2,
    Completed = 3,
    Cancelled = 4,
}

public sealed record CampaignInput(string Name, string? Description, CampaignStatus Status, DateOnly? StartOn, DateOnly? EndOn, decimal? GoalAmount, Guid? OwnerUserId);

/// <summary>
/// Belirli bir amaç için yürütülen çalışma (yardım kampanyası, üye kazanımı, imza toplama). Fon, etkinlik, toplu ileti
/// ve formlar kampanyaya bağlanır; sonuçlar bağlı kayıtların sahibi modüllerden toplanır.
/// </summary>
public sealed class Campaign : Entity
{
    private Campaign()
    {
    }

    public string Name { get; private set; } = "";

    public string? Description { get; private set; }

    public CampaignStatus Status { get; private set; }

    public DateOnly? StartOn { get; private set; }

    public DateOnly? EndOn { get; private set; }

    /// <summary>TL hedef; bağış ve bilet geliri bu hedefe sayılır.</summary>
    public decimal? GoalAmount { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public List<CampaignLink> Links { get; private set; } = [];

    public static Result<Campaign> Create(CampaignInput input)
    {
        var campaign = new Campaign();
        var updated = campaign.Update(input);
        return updated.IsSuccess ? campaign : updated.Error!;
    }

    public Result<bool> Update(CampaignInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Name))
        {
            return new Error("campaigns.name", "Kampanyaya bir ad verin.");
        }

        if (input.StartOn is { } start && input.EndOn is { } end && end < start)
        {
            return new Error("campaigns.period", "Bitiş tarihi başlangıçtan önce olamaz.");
        }

        if (input.GoalAmount is <= 0)
        {
            return new Error("campaigns.goal", "Hedef tutar sıfırdan büyük olmalı.");
        }

        Name = input.Name.Trim();
        Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        Status = input.Status;
        StartOn = input.StartOn;
        EndOn = input.EndOn;
        GoalAmount = input.GoalAmount;
        OwnerUserId = input.OwnerUserId;
        return true;
    }

    public bool Link(string kind, Guid itemId)
    {
        if (Links.Any(l => l.Kind == kind && l.ItemId == itemId))
        {
            return false;
        }

        Links.Add(CampaignLink.Create(Id, kind, itemId));
        return true;
    }

    public void Unlink(Guid linkId) => Links.RemoveAll(l => l.Id == linkId);
}

/// <summary>Kampanyaya bağlı kayıt: tür ("fund", "event") ve kaydın kimliği.</summary>
public sealed class CampaignLink : Entity
{
    private CampaignLink()
    {
    }

    public Guid CampaignId { get; private set; }

    public string Kind { get; private set; } = "";

    public Guid ItemId { get; private set; }

    internal static CampaignLink Create(Guid campaignId, string kind, Guid itemId) => new() { CampaignId = campaignId, Kind = kind, ItemId = itemId };
}
