using Arma.SharedKernel.Domain;

namespace Arma.Modules.Sales.Domain;

public enum StageKind
{
    Open = 1,
    Won = 2,
    Lost = 3,
}

/// <summary>Satış süreci: aşamalardan oluşur. Her hatta en az bir açık, bir kazanıldı ve bir kaybedildi aşaması vardır.</summary>
public sealed class Pipeline : Entity
{
    private Pipeline()
    {
    }

    public string Name { get; private set; } = "";

    public bool IsDefault { get; private set; }

    public List<Stage> Stages { get; private set; } = [];

    public static Pipeline CreateDefault() => new()
    {
        Name = "Satış",
        IsDefault = true,
        Stages =
        [
            Stage.Create("Yeni", 1, 10, StageKind.Open),
            Stage.Create("Görüşme", 2, 30, StageKind.Open),
            Stage.Create("Teklif", 3, 60, StageKind.Open),
            Stage.Create("Pazarlık", 4, 80, StageKind.Open),
            Stage.Create("Kazanıldı", 5, 100, StageKind.Won),
            Stage.Create("Kaybedildi", 6, 0, StageKind.Lost),
        ],
    };

    public static Result<Pipeline> Create(string name)
    {
        var pipeline = CreateDefault();
        pipeline.IsDefault = false;
        var renamed = pipeline.Rename(name);
        return renamed.IsSuccess ? pipeline : renamed.Error!;
    }

    public Result<bool> Rename(string name)
    {
        name = name?.Trim() ?? "";
        if (name.Length is 0 or > 100)
        {
            return new Error("pipelines.name", "Satış hattı adı 1-100 karakter olmalı.");
        }

        Name = name;
        return true;
    }

    /// <summary>Aşamaları verilen sırayla değiştirir. Kimliği olan aşama güncellenir, olmayan eklenir, listede olmayan silinir.</summary>
    public Result<IReadOnlyList<Guid>> ReplaceStages(IReadOnlyList<StageInput> inputs)
    {
        if (inputs.Count(s => s.Kind == StageKind.Open) == 0 || inputs.Count(s => s.Kind == StageKind.Won) != 1 || inputs.Count(s => s.Kind == StageKind.Lost) != 1)
        {
            return new Error("pipelines.stages", "En az bir açık aşama, bir \"kazanıldı\" ve bir \"kaybedildi\" aşaması olmalı.");
        }

        if (inputs.Any(s => string.IsNullOrWhiteSpace(s.Name) || s.Name.Trim().Length > 60 || s.Probability is < 0 or > 100))
        {
            return new Error("pipelines.stage", "Aşama adı 1-60 karakter, olasılık 0-100 olmalı.");
        }

        var removed = Stages.Where(s => inputs.All(i => i.Id != s.Id)).Select(s => s.Id).ToList();
        Stages.RemoveAll(s => removed.Contains(s.Id));
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var existing = input.Id is { } id ? Stages.FirstOrDefault(s => s.Id == id) : null;
            if (existing is null)
            {
                Stages.Add(Stage.Create(input.Name.Trim(), i + 1, input.Probability, input.Kind));
            }
            else
            {
                existing.Update(input.Name.Trim(), i + 1, input.Probability, input.Kind);
            }
        }

        return removed;
    }

    public void MakeDefault(bool value) => IsDefault = value;
}

public sealed record StageInput(Guid? Id, string Name, int Probability, StageKind Kind);

public sealed class Stage : Entity
{
    private Stage()
    {
    }

    public Guid PipelineId { get; private set; }

    public string Name { get; private set; } = "";

    public int Position { get; private set; }

    /// <summary>Bu aşamadaki fırsatın kazanılma olasılığı (%); ağırlıklı tahmin için.</summary>
    public int Probability { get; private set; }

    public StageKind Kind { get; private set; }

    internal static Stage Create(string name, int position, int probability, StageKind kind) =>
        new() { Name = name, Position = position, Probability = probability, Kind = kind };

    internal void Update(string name, int position, int probability, StageKind kind) =>
        (Name, Position, Probability, Kind) = (name, position, probability, kind);
}

/// <summary>Satış fırsatı: bir kişi veya kurumla olası iş.</summary>
public sealed class Opportunity : Entity
{
    private Opportunity()
    {
    }

    public string Title { get; private set; } = "";

    public Guid ContactId { get; private set; }

    public Guid PipelineId { get; private set; }

    public Guid StageId { get; private set; }

    public decimal Amount { get; private set; }

    public DateOnly? ExpectedCloseOn { get; private set; }

    public Guid? OwnerUserId { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Kazanıldı veya kaybedildi aşamasına geçtiği gün.</summary>
    public DateOnly? ClosedOn { get; private set; }

    public string? LostReason { get; private set; }

    /// <summary>Panodaki sıra (aşama içinde).</summary>
    public double SortKey { get; private set; }

    public static Result<Opportunity> Create(OpportunityInput input, Guid pipelineId, Guid stageId, double sortKey)
    {
        var item = new Opportunity { PipelineId = pipelineId, StageId = stageId, SortKey = sortKey };
        var result = item.Update(input);
        return result.IsSuccess ? item : result.Error!;
    }

    public Result<bool> Update(OpportunityInput input)
    {
        var title = input.Title?.Trim() ?? "";
        if (title.Length is 0 or > 200)
        {
            return new Error("opportunities.title", "Fırsat adı 1-200 karakter olmalı.");
        }

        if (input.Amount is < 0 or > 1_000_000_000_000)
        {
            return new Error("opportunities.amount", "Tutar geçerli değil.");
        }

        Title = title;
        ContactId = input.ContactId;
        Amount = decimal.Round(input.Amount, 2);
        ExpectedCloseOn = input.ExpectedCloseOn;
        OwnerUserId = input.OwnerUserId;
        Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        return true;
    }

    /// <summary>Aşamayı (ve panodaki sırasını) değiştirir. Kapanış aşamasına geçen fırsat bugünün tarihiyle kapanır.</summary>
    public void MoveTo(Stage stage, double sortKey, DateOnly today, string? lostReason)
    {
        if (stage.Kind == StageKind.Open)
        {
            ClosedOn = null;
        }
        else if (StageId != stage.Id || ClosedOn is null)
        {
            ClosedOn = today;
        }

        LostReason = stage.Kind != StageKind.Lost ? null : string.IsNullOrWhiteSpace(lostReason) ? LostReason : lostReason.Trim();
        StageId = stage.Id;
        SortKey = sortKey;
    }

    public void ChangeContact(Guid contactId) => ContactId = contactId;

    public void ClearNote() => (Note, LostReason) = (null, null);
}

public sealed record OpportunityInput(string Title, Guid ContactId, decimal Amount, DateOnly? ExpectedCloseOn, Guid? OwnerUserId, string? Note);

public enum QuoteStatus
{
    Draft = 1,
    Sent = 2,
    Accepted = 3,
    Rejected = 4,
}

/// <summary>Müşteriye verilen fiyat teklifi. Numara yıl içinde sıralıdır (T2026-0001).</summary>
public sealed class Quote : Entity
{
    private Quote()
    {
    }

    public string Number { get; private set; } = "";

    public Guid ContactId { get; private set; }

    public Guid? OpportunityId { get; private set; }

    public DateOnly IssuedOn { get; private set; }

    public DateOnly ValidUntil { get; private set; }

    public QuoteStatus Status { get; private set; } = QuoteStatus.Draft;

    public string? Terms { get; private set; }

    public List<QuoteLine> Lines { get; private set; } = [];

    public decimal Subtotal => Lines.Sum(l => l.Net);

    public decimal VatTotal => Lines.Sum(l => l.Vat);

    public decimal Total => Subtotal + VatTotal;

    public static Result<Quote> Create(string number, QuoteInput input)
    {
        var quote = new Quote { Number = number };
        var result = quote.Update(input);
        return result.IsSuccess ? quote : result.Error!;
    }

    public Result<bool> Update(QuoteInput input)
    {
        if (Status is QuoteStatus.Accepted or QuoteStatus.Rejected)
        {
            return new Error("quotes.closed", "Kabul veya ret edilmiş teklif değiştirilemez.");
        }

        if (input.Lines.Count == 0)
        {
            return new Error("quotes.lines", "Teklifte en az bir kalem olmalı.");
        }

        if (input.ValidUntil < input.IssuedOn)
        {
            return new Error("quotes.dates", "Geçerlilik tarihi teklif tarihinden önce olamaz.");
        }

        var lines = new List<QuoteLine>();
        foreach (var line in input.Lines)
        {
            var created = QuoteLine.Create(line, lines.Count + 1);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            lines.Add(created.Value!);
        }

        ContactId = input.ContactId;
        OpportunityId = input.OpportunityId;
        IssuedOn = input.IssuedOn;
        ValidUntil = input.ValidUntil;
        Terms = string.IsNullOrWhiteSpace(input.Terms) ? null : input.Terms.Trim();
        Lines.Clear();
        Lines.AddRange(lines);
        return true;
    }

    public Result<bool> SetStatus(QuoteStatus status)
    {
        if (status == QuoteStatus.Draft && Status != QuoteStatus.Draft)
        {
            return new Error("quotes.status", "Gönderilmiş teklif taslağa döndürülemez.");
        }

        Status = status;
        return true;
    }

    public void ChangeContact(Guid contactId) => ContactId = contactId;
}

public sealed record QuoteInput(Guid ContactId, Guid? OpportunityId, DateOnly IssuedOn, DateOnly ValidUntil, string? Terms, IReadOnlyList<QuoteLineInput> Lines);

/// <param name="VatRate">KDV oranı (%): 0, 1, 10 veya 20.</param>
/// <param name="DiscountRate">İskonto (%).</param>
public sealed record QuoteLineInput(string Description, decimal Quantity, string? Unit, decimal UnitPrice, decimal DiscountRate, int VatRate);

public sealed class QuoteLine : Entity
{
    public static readonly int[] VatRates = [0, 1, 10, 20];

    private QuoteLine()
    {
    }

    public Guid QuoteId { get; private set; }

    public int Position { get; private set; }

    public string Description { get; private set; } = "";

    public decimal Quantity { get; private set; }

    public string? Unit { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal DiscountRate { get; private set; }

    public int VatRate { get; private set; }

    public decimal Net => NetOf(Quantity, UnitPrice, DiscountRate);

    public decimal Vat => VatOf(Net, VatRate);

    public static decimal NetOf(QuoteLineInput input) => NetOf(input.Quantity, input.UnitPrice, input.DiscountRate);

    public static decimal VatOf(QuoteLineInput input) => VatOf(NetOf(input), input.VatRate);

    private static decimal NetOf(decimal quantity, decimal unitPrice, decimal discountRate) =>
        decimal.Round(quantity * unitPrice * (1 - discountRate / 100m), 2, MidpointRounding.AwayFromZero);

    private static decimal VatOf(decimal net, int vatRate) => decimal.Round(net * vatRate / 100m, 2, MidpointRounding.AwayFromZero);

    internal static Result<QuoteLine> Create(QuoteLineInput input, int position)
    {
        var description = input.Description?.Trim() ?? "";
        if (description.Length is 0 or > 500)
        {
            return new Error("quotes.line_description", $"{position}. kalemin açıklaması 1-500 karakter olmalı.");
        }

        if (input.Quantity <= 0 || input.UnitPrice < 0 || input.DiscountRate is < 0 or > 100)
        {
            return new Error("quotes.line_amount", $"{position}. kalemde miktar, fiyat veya iskonto geçerli değil.");
        }

        if (!VatRates.Contains(input.VatRate))
        {
            return new Error("quotes.line_vat", $"{position}. kalemde KDV oranı 0, 1, 10 veya 20 olmalı.");
        }

        return new QuoteLine
        {
            Position = position,
            Description = description,
            Quantity = input.Quantity,
            Unit = string.IsNullOrWhiteSpace(input.Unit) ? null : input.Unit.Trim(),
            UnitPrice = decimal.Round(input.UnitPrice, 2),
            DiscountRate = input.DiscountRate,
            VatRate = input.VatRate,
        };
    }
}

/// <summary>Teklif başlığındaki kurum bilgileri ve varsayılan şartlar; kurum başına tek kayıt.</summary>
public sealed class QuoteSettings : Entity
{
    public string OrganizationName { get; set; } = "";

    public string? Address { get; set; }

    /// <summary>Vergi dairesi ve numarası, MERSİS no gibi.</summary>
    public string? LegalInfo { get; set; }

    /// <summary>Yeni teklife önceden yazılan ödeme ve teslim şartları.</summary>
    public string? DefaultTerms { get; set; }

    /// <summary>Yeni teklifin kaç gün geçerli olacağı.</summary>
    public int ValidityDays { get; set; } = 30;
}
