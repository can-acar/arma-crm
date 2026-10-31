using Arma.Infrastructure.Messaging;
using Arma.Modules.Activities.Contracts;
using Arma.Modules.Cases.Domain;
using Arma.Modules.Cases.Infrastructure;
using Arma.Modules.Contacts.Contracts;
using Arma.Modules.Identity.Contracts;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Wolverine.EntityFrameworkCore;

namespace Arma.Modules.Cases.Application;

public sealed record CaseTypeView(Guid Id, string Name, string? Description, bool IsActive, IReadOnlyList<StepTemplate> Steps, int OpenCases);

public sealed record SaveCaseTypeRequest(string Name, string? Description, bool IsActive, IReadOnlyList<StepTemplate> Steps);

public sealed record CaseFilter(
    CaseStatus? Status = null,
    bool OpenOnly = false,
    Guid? TypeId = null,
    Guid? AssigneeId = null,
    bool Unassigned = false,
    Guid? ContactId = null,
    bool OverdueOnly = false,
    string? Search = null,
    int Page = 1,
    int PageSize = 50);

public sealed record CaseListItem(
    Guid Id,
    string Number,
    string Subject,
    Guid TypeId,
    string TypeName,
    Guid ContactId,
    string ContactName,
    CaseStatus Status,
    CasePriority Priority,
    Guid? AssigneeId,
    string? AssigneeName,
    DateOnly OpenedOn,
    DateOnly? ClosedOn,
    int StepsDone,
    int StepsTotal,
    DateOnly? NextDueOn,
    bool IsOverdue);

public sealed record CasePage(IReadOnlyList<CaseListItem> Items, int Total);

public sealed record CaseStepView(Guid Id, string Title, DateOnly? DueOn, int Order, DateTimeOffset? CompletedAt, string? CompletedBy, bool IsOverdue);

public sealed record CaseDetail(CaseListItem Summary, string? Description, string? Outcome, IReadOnlyList<CaseStepView> Steps, DateTimeOffset CreatedAt, string? CreatedBy);

public sealed record CaseNoteView(Guid Id, string Body, bool IsConfidential, DateTimeOffset CreatedAt, string? CreatedBy);

public sealed record OpenCaseRequest(Guid ContactId, Guid TypeId, string Subject, string? Description, CasePriority Priority, Guid? AssigneeId);

public sealed record UpdateCaseRequest(string Subject, string? Description, CasePriority Priority, Guid? AssigneeId);

public sealed record CaseSummary(int Open, int Overdue, int Mine, int MineOverdue);

/// <summary>
/// Vaka türleri, vakalar, adımlar ve notlar. Yetki denetimi sayfalarda yapılır; gizli notları görmeyen kullanıcılar için
/// <see cref="NotesAsync"/> çağrısında <c>includeConfidential</c> false verilir.
/// </summary>
public sealed class CaseService(
    CasesDbContext db,
    IContactDirectory contacts,
    IUserDirectory users,
    IDbContextOutbox outbox,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    internal const string SourceModule = "Cases";

    public DateOnly Today => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    public async Task<IReadOnlyList<CaseTypeView>> TypesAsync(bool activeOnly = false, CancellationToken cancellationToken = default)
    {
        var types = await db.Types.AsNoTracking().Where(t => !activeOnly || t.IsActive).OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var open = await db.Cases.Where(c => c.Status != CaseStatus.Closed).GroupBy(c => c.TypeId)
            .Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return [.. types.Select(t => new CaseTypeView(t.Id, t.Name, t.Description, t.IsActive, t.Steps, open.GetValueOrDefault(t.Id)))];
    }

    public async Task<Result<Guid>> SaveTypeAsync(Guid? id, SaveCaseTypeRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name?.Trim() ?? "";
        var turkish = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        var others = await db.Types.Where(t => t.Id != id).Select(t => t.Name).ToListAsync(cancellationToken);
        if (others.Any(other => string.Compare(other, name, turkish, System.Globalization.CompareOptions.IgnoreCase) == 0))
        {
            return CaseErrors.DuplicateType;
        }

        CaseType type;
        if (id is null)
        {
            var created = CaseType.Create(name, request.Description, request.Steps);
            if (!created.IsSuccess)
            {
                return created.Error!;
            }

            type = created.Value!;
            db.Types.Add(type);
        }
        else
        {
            type = await db.Types.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new KeyNotFoundException();
            var updated = type.Update(name, request.Description, request.IsActive, request.Steps);
            if (!updated.IsSuccess)
            {
                return updated.Error!;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return type.Id;
    }

    public async Task<CasePage> ListAsync(CaseFilter filter, CancellationToken cancellationToken = default)
    {
        var query = db.Cases.AsNoTracking();
        if (filter.Status is { } status)
        {
            query = query.Where(c => c.Status == status);
        }
        else if (filter.OpenOnly)
        {
            query = query.Where(c => c.Status != CaseStatus.Closed);
        }

        if (filter.TypeId is { } typeId)
        {
            query = query.Where(c => c.TypeId == typeId);
        }

        if (filter.AssigneeId is { } assigneeId)
        {
            query = query.Where(c => c.AssigneeId == assigneeId);
        }
        else if (filter.Unassigned)
        {
            query = query.Where(c => c.AssigneeId == null);
        }

        if (filter.ContactId is { } contactId)
        {
            query = query.Where(c => c.ContactId == contactId);
        }

        var today = Today;
        if (filter.OverdueOnly)
        {
            query = query.Where(c => c.Status != CaseStatus.Closed && c.Steps.Any(s => s.CompletedAt == null && s.DueOn < today));
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var matches = await contacts.SearchAsync(term, 50, cancellationToken);
            var ids = matches.Select(m => m.Id).ToList();
            var pattern = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(c => EF.Functions.ILike(c.Subject, pattern) || EF.Functions.ILike(c.Number, pattern) || ids.Contains(c.ContactId));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(c => c.Status == CaseStatus.Closed).ThenByDescending(c => c.Priority).ThenByDescending(c => c.OpenedOn).ThenByDescending(c => c.Number)
            .Skip((Math.Max(filter.Page, 1) - 1) * filter.PageSize).Take(filter.PageSize)
            .ToListAsync(cancellationToken);
        return new(await ToItemsAsync(rows, cancellationToken), total);
    }

    public async Task<IReadOnlyList<CaseListItem>> ForContactAsync(Guid contactId, CancellationToken cancellationToken = default) =>
        (await ListAsync(new CaseFilter(ContactId: contactId, PageSize: 200), cancellationToken)).Items;

    public async Task<CaseDetail?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.Cases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var today = Today;
        var summary = (await ToItemsAsync([item], cancellationToken))[0];
        var names = await UserNamesAsync([item.CreatedBy, .. item.Steps.Select(s => s.CompletedBy)], cancellationToken);
        var steps = item.Steps.OrderBy(s => s.Order).ThenBy(s => s.CreatedAt)
            .Select(s => new CaseStepView(s.Id, s.Title, s.DueOn, s.Order, s.CompletedAt, Name(names, s.CompletedBy), IsOverdue(s, today))).ToList();
        return new(summary, item.Description, item.Outcome, steps, item.CreatedAt, Name(names, item.CreatedBy));
    }

    public async Task<Result<Guid>> OpenAsync(OpenCaseRequest request, CancellationToken cancellationToken = default)
    {
        var type = await db.Types.FirstOrDefaultAsync(t => t.Id == request.TypeId && t.IsActive, cancellationToken);
        if (type is null)
        {
            return CaseErrors.TypeNotFound;
        }

        var contact = await contacts.FindAsync(request.ContactId, cancellationToken);
        if (contact is null)
        {
            return CaseErrors.ContactNotFound;
        }

        if (await CheckAssigneeAsync(request.AssigneeId, null, cancellationToken) is { } assigneeError)
        {
            return assigneeError;
        }

        var today = Today;
        var number = $"V{today.Year}-{await db.NextCaseNumberAsync(today.Year, cancellationToken):0000}";
        var opened = Case.Open(number, type, contact.Id, request.Subject, request.Description, request.Priority, request.AssigneeId, today);
        if (!opened.IsSuccess)
        {
            return opened.Error!;
        }

        var item = opened.Value!;
        db.Cases.Add(item);
        outbox.Enroll(db);
        await outbox.PublishAsync(Timeline(item, $"Vaka açıldı: {item.Number} {item.Subject}", type.Name, today, "open"));
        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return item.Id;
    }

    public async Task<Result<Guid>> UpdateAsync(Guid id, UpdateCaseRequest request, CancellationToken cancellationToken = default)
    {
        var item = await db.Cases.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (item is null)
        {
            return CaseErrors.NotFound;
        }

        if (await CheckAssigneeAsync(request.AssigneeId, item.AssigneeId, cancellationToken) is { } assigneeError)
        {
            return assigneeError;
        }

        var updated = item.Update(request.Subject, request.Description, request.Priority, request.AssigneeId);
        if (!updated.IsSuccess)
        {
            return updated.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);
        return id;
    }

    /// <summary>Durumu değiştirir; kapanış ve yeniden açılış kişinin zaman çizelgesine düşer.</summary>
    public async Task<Result<Guid>> ChangeStatusAsync(Guid id, CaseStatus status, string? outcome, CancellationToken cancellationToken = default)
    {
        var item = await db.Cases.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (item is null)
        {
            return CaseErrors.NotFound;
        }

        var wasClosed = item.Status == CaseStatus.Closed;
        var today = Today;
        var changed = item.ChangeStatus(status, outcome, today);
        if (!changed.IsSuccess)
        {
            return changed.Error!;
        }

        outbox.Enroll(db);
        if (status == CaseStatus.Closed && !wasClosed)
        {
            await outbox.PublishAsync(Timeline(item, $"Vaka kapandı: {item.Number} {item.Subject}", item.Outcome, today, $"close:{today:yyyyMMdd}"));
        }
        else if (wasClosed && status != CaseStatus.Closed)
        {
            await outbox.PublishAsync(Timeline(item, $"Vaka yeniden açıldı: {item.Number} {item.Subject}", null, today, $"reopen:{today:yyyyMMdd}"));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return id;
    }

    public async Task<Result<Guid>> AddStepAsync(Guid caseId, string title, DateOnly? dueOn, CancellationToken cancellationToken = default)
    {
        var item = await db.Cases.FirstOrDefaultAsync(c => c.Id == caseId, cancellationToken);
        if (item is null)
        {
            return CaseErrors.NotFound;
        }

        if (item.Status == CaseStatus.Closed)
        {
            return CaseErrors.Closed;
        }

        var step = CaseStep.Create(caseId, title, dueOn, item.Steps.Count == 0 ? 0 : item.Steps.Max(s => s.Order) + 1);
        if (!step.IsSuccess)
        {
            return step.Error!;
        }

        db.Steps.Add(step.Value!);
        await db.SaveChangesAsync(cancellationToken);
        return step.Value!.Id;
    }

    public async Task<Result<Guid>> SetStepDoneAsync(Guid stepId, bool done, CancellationToken cancellationToken = default)
    {
        var step = await db.Steps.FirstOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (step is null)
        {
            return CaseErrors.NotFound;
        }

        if (await db.Cases.AnyAsync(c => c.Id == step.CaseId && c.Status == CaseStatus.Closed, cancellationToken))
        {
            return CaseErrors.Closed;
        }

        if (done && !step.IsDone)
        {
            step.Complete(clock.GetUtcNow(), currentUser.Id);
        }
        else if (!done)
        {
            step.Reopen();
        }

        await db.SaveChangesAsync(cancellationToken);
        return stepId;
    }

    public async Task<bool> DeleteStepAsync(Guid stepId, CancellationToken cancellationToken = default)
    {
        var step = await db.Steps.FirstOrDefaultAsync(s => s.Id == stepId, cancellationToken);
        if (step is null)
        {
            return false;
        }

        step.IsDeleted = true;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CaseNoteView>> NotesAsync(Guid caseId, bool includeConfidential, CancellationToken cancellationToken = default)
    {
        var notes = await db.Notes.AsNoTracking()
            .Where(n => n.CaseId == caseId && (includeConfidential || !n.IsConfidential))
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
        var names = await UserNamesAsync([.. notes.Select(n => n.CreatedBy)], cancellationToken);
        return [.. notes.Select(n => new CaseNoteView(n.Id, n.Body, n.IsConfidential, n.CreatedAt, Name(names, n.CreatedBy)))];
    }

    /// <summary>Gizli olmayan not kişinin zaman çizelgesine de düşer; gizli not yalnızca vakada kalır.</summary>
    public async Task<Result<Guid>> AddNoteAsync(Guid caseId, string body, bool isConfidential, CancellationToken cancellationToken = default)
    {
        var item = await db.Cases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caseId, cancellationToken);
        if (item is null)
        {
            return CaseErrors.NotFound;
        }

        var note = CaseNote.Create(caseId, body, isConfidential);
        if (!note.IsSuccess)
        {
            return note.Error!;
        }

        db.Notes.Add(note.Value!);
        outbox.Enroll(db);
        if (!isConfidential)
        {
            await outbox.PublishAsync(new RecordTimelineEntry([item.ContactId], ActivityKind.Note, $"Vaka notu: {item.Number}", note.Value!.Body,
                clock.GetLocalNow(), SourceModule, $"note:{note.Value!.Id}"));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return note.Value!.Id;
    }

    public async Task<bool> DeleteNoteAsync(Guid noteId, CancellationToken cancellationToken = default)
    {
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);
        if (note is null)
        {
            return false;
        }

        note.IsDeleted = true;
        outbox.Enroll(db);
        if (!note.IsConfidential)
        {
            await outbox.PublishAsync(new RemoveTimelineEntry(SourceModule, $"note:{note.Id}"));
        }

        await outbox.SaveChangesAndPublishAsync(cancellationToken);
        return true;
    }

    /// <param name="userId">Panodaki "bana atanan" sayıları için; boşsa sıfır döner.</param>
    public async Task<CaseSummary> SummaryAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        var today = Today;
        var open = db.Cases.Where(c => c.Status != CaseStatus.Closed);
        var overdue = open.Where(c => c.Steps.Any(s => s.CompletedAt == null && s.DueOn < today));
        return new(
            await open.CountAsync(cancellationToken),
            await overdue.CountAsync(cancellationToken),
            userId is null ? 0 : await open.CountAsync(c => c.AssigneeId == userId, cancellationToken),
            userId is null ? 0 : await overdue.CountAsync(c => c.AssigneeId == userId, cancellationToken));
    }

    public async Task<IReadOnlyList<UserSummary>> AssignableUsersAsync(CancellationToken cancellationToken = default) =>
        await users.ActiveUsersAsync(cancellationToken);

    internal async Task MoveContactAsync(Guid from, Guid to, CancellationToken cancellationToken)
    {
        var cases = await db.Cases.Where(c => c.ContactId == from).ToListAsync(cancellationToken);
        foreach (var item in cases)
        {
            item.ChangeContact(to);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Error?> CheckAssigneeAsync(Guid? assigneeId, Guid? current, CancellationToken cancellationToken)
    {
        if (assigneeId is null || assigneeId == current)
        {
            return null;
        }

        var found = await users.FindManyAsync([assigneeId.Value], cancellationToken);
        return found.TryGetValue(assigneeId.Value, out var user) && user.IsActive
            ? null
            : new Error("cases.assignee", "Sorumlu kullanıcı bulunamadı veya pasif.");
    }

    private async Task<IReadOnlyList<CaseListItem>> ToItemsAsync(IReadOnlyList<Case> rows, CancellationToken cancellationToken)
    {
        var today = Today;
        var typeIds = rows.Select(r => r.TypeId).Distinct().ToList();
        var types = await db.Types.IgnoreQueryFilters().AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
        var people = await contacts.FindManyAsync([.. rows.Select(r => r.ContactId).Distinct()], cancellationToken);
        var assignees = await users.FindManyAsync([.. rows.Where(r => r.AssigneeId is not null).Select(r => r.AssigneeId!.Value).Distinct()], cancellationToken);
        return [.. rows.Select(c =>
        {
            var pending = c.Steps.Where(s => !s.IsDone).ToList();
            return new CaseListItem(c.Id, c.Number, c.Subject, c.TypeId, types.GetValueOrDefault(c.TypeId, "?"), c.ContactId,
                people.TryGetValue(c.ContactId, out var person) ? person.DisplayName : "Silinmiş kişi", c.Status, c.Priority, c.AssigneeId,
                c.AssigneeId is { } a && assignees.TryGetValue(a, out var user) ? user.FullName : null, c.OpenedOn, c.ClosedOn,
                c.Steps.Count(s => s.IsDone), c.Steps.Count, pending.Where(s => s.DueOn is not null).Min(s => s.DueOn),
                c.Status != CaseStatus.Closed && pending.Any(s => IsOverdue(s, today)));
        })];
    }

    /// <summary>Kayıtlarda kullanıcı kimliği tutulur; ekranda adı gösterilir.</summary>
    private async Task<IReadOnlyDictionary<Guid, UserSummary>> UserNamesAsync(IEnumerable<string?> ids, CancellationToken cancellationToken)
    {
        var parsed = ids.Select(id => Guid.TryParse(id, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Distinct().ToList();
        return parsed.Count == 0 ? new Dictionary<Guid, UserSummary>() : await users.FindManyAsync(parsed, cancellationToken);
    }

    private static string? Name(IReadOnlyDictionary<Guid, UserSummary> names, string? id) =>
        Guid.TryParse(id, out var g) && names.TryGetValue(g, out var user) ? user.FullName : id;

    private static bool IsOverdue(CaseStep step, DateOnly today) => !step.IsDone && step.DueOn < today;

    private RecordTimelineEntry Timeline(Case item, string subject, string? body, DateOnly on, string suffix)
    {
        var now = clock.GetLocalNow();
        var at = DateOnly.FromDateTime(now.DateTime) == on ? now : new DateTimeOffset(on.ToDateTime(TimeOnly.MinValue), now.Offset);
        return new RecordTimelineEntry([item.ContactId], ActivityKind.System, subject, body, at, SourceModule, $"{item.Id}:{suffix}");
    }
}
