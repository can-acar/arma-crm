using System.Text.Json;
using Arma.Infrastructure.Auditing;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Security;
using Arma.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Audit;

public sealed record AuditQuery(string? EntityType = null, string? EntityId = null, string? User = null, AuditAction? Action = null, DateOnly? From = null, DateOnly? To = null, int Page = 1, int PageSize = 50);

public sealed record AuditPage(IReadOnlyList<AuditRecord> Items, int Total, int Page, int PageSize);

public sealed class AuditLogService(AuditDbContext db, ITenantContext tenant, ICurrentUser user, IHttpContextAccessor http, TimeProvider clock) : IAuditLog
{
    public async Task WriteAsync(AuditAction action, string entityType, string? entityId, string? summary, CancellationToken cancellationToken = default)
    {
        db.Entries.Add(new AuditEntry
        {
            TenantId = tenant.TenantId,
            OccurredAt = clock.GetUtcNow(),
            UserId = user.Id,
            UserName = user.Name,
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditRecord>> ForEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        var rows = await db.Entries.AsNoTracking()
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .OrderByDescending(e => e.OccurredAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        return rows.Select(ToRecord).ToList();
    }

    public async Task<AuditPage> SearchAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var rows = db.Entries.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            rows = rows.Where(e => e.EntityType == query.EntityType);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            rows = rows.Where(e => e.EntityId == query.EntityId);
        }

        if (!string.IsNullOrWhiteSpace(query.User))
        {
            var term = query.User.Trim();
            rows = rows.Where(e => e.UserName != null && EF.Functions.ILike(e.UserName, $"%{term}%"));
        }

        if (query.Action is { } action)
        {
            rows = rows.Where(e => e.Action == action);
        }

        if (query.From is { } from)
        {
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            rows = rows.Where(e => e.OccurredAt >= start);
        }

        if (query.To is { } to)
        {
            var end = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            rows = rows.Where(e => e.OccurredAt < end);
        }

        var page = Math.Max(1, query.Page);
        var size = Math.Clamp(query.PageSize, 1, 200);
        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderByDescending(e => e.OccurredAt).Skip((page - 1) * size).Take(size).ToListAsync(cancellationToken);
        return new AuditPage(items.Select(ToRecord).ToList(), total, page, size);
    }

    public async Task<IReadOnlyList<string>> EntityTypesAsync(CancellationToken cancellationToken = default) =>
        await db.Entries.AsNoTracking().Select(e => e.EntityType).Distinct().OrderBy(t => t).ToListAsync(cancellationToken);

    private static AuditRecord ToRecord(AuditEntry e) => new(
        e.Id, e.OccurredAt, e.UserName, e.IpAddress, e.Action, e.EntityType, e.EntityId, e.Summary, ParseChanges(e.Changes));

    private static IReadOnlyList<AuditChange> ParseChanges(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Select(c => new AuditChange(c.GetProperty("p").GetString()!, Read(c, "o"), Read(c, "n")))
            .ToList();
    }

    private static string? Read(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
