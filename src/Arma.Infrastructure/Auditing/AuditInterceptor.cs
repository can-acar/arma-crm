using System.Reflection;
using System.Text.Json;
using Arma.SharedKernel.Auditing;
using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Security;
using Arma.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Arma.Infrastructure.Auditing;

/// <summary>
/// <see cref="Entity"/> türevlerindeki her eklemeyi, değişikliği ve silmeyi aynı kayıt işleminde
/// denetim tablosuna yazar. Hassas alanların değeri yazılmaz.
/// </summary>
public sealed class AuditInterceptor(ITenantContext tenant, ICurrentUser user, IHttpContextAccessor http, TimeProvider clock) : SaveChangesInterceptor
{
    private const string Masked = "***";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Capture(DbContext? context)
    {
        if (context?.Model.FindEntityType(typeof(AuditEntry)) is null)
        {
            return;
        }

        var entries = context.ChangeTracker.Entries<Entity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .Select(ToAuditEntry)
            .OfType<AuditEntry>()
            .ToList();

        context.Set<AuditEntry>().AddRange(entries);
    }

    private AuditEntry? ToAuditEntry(EntityEntry<Entity> entry)
    {
        var deleted = entry.State == EntityState.Modified
                      && entry.Property(nameof(Entity.IsDeleted)) is { IsModified: true, CurrentValue: true };
        var action = entry.State == EntityState.Added ? AuditAction.Created : deleted ? AuditAction.Deleted : AuditAction.Updated;

        var changes = new List<object>();
        foreach (var property in entry.Properties)
        {
            var info = property.Metadata.PropertyInfo;
            if (property.Metadata.IsPrimaryKey() || IsBookkeeping(property.Metadata.Name) || info?.GetCustomAttribute<NotAuditedAttribute>() is not null)
            {
                continue;
            }

            if (action == AuditAction.Updated && !property.IsModified)
            {
                continue;
            }

            if (action == AuditAction.Updated && Equals(property.OriginalValue, property.CurrentValue))
            {
                continue;
            }

            if (action == AuditAction.Created && property.CurrentValue is null)
            {
                continue;
            }

            var sensitive = info?.GetCustomAttribute<SensitiveDataAttribute>() is not null;
            changes.Add(new
            {
                p = property.Metadata.Name,
                o = action == AuditAction.Created ? null : sensitive ? Masked : Format(property.OriginalValue),
                n = sensitive ? Masked : Format(property.CurrentValue),
            });
        }

        // Owned JSON kolonları (adres listesi gibi) ayrı izlenir.
        foreach (var reference in entry.References.Where(r => r.Metadata.TargetEntityType.IsOwned() && (r.IsModified || action == AuditAction.Created)))
        {
            changes.Add(new { p = reference.Metadata.Name, o = (string?)null, n = Format(reference.CurrentValue) });
        }

        foreach (var collection in entry.Collections.Where(c => c.Metadata.TargetEntityType.IsOwned() && (c.IsModified || action == AuditAction.Created)))
        {
            changes.Add(new { p = collection.Metadata.Name, o = (string?)null, n = Format(collection.CurrentValue) });
        }

        if (action == AuditAction.Updated && changes.Count == 0)
        {
            return null;
        }

        return new AuditEntry
        {
            TenantId = tenant.TenantId,
            OccurredAt = clock.GetUtcNow(),
            UserId = user.Id,
            UserName = user.Name,
            IpAddress = http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Action = action,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = entry.Entity.Id.ToString(),
            Changes = JsonSerializer.Serialize(changes),
        };
    }

    private static bool IsBookkeeping(string name) => name is nameof(Entity.TenantId) or nameof(Entity.CreatedAt) or nameof(Entity.CreatedBy)
        or nameof(Entity.UpdatedAt) or nameof(Entity.UpdatedBy) or nameof(Entity.IsDeleted);

    private static string? Format(object? value) => value switch
    {
        null => null,
        string s => s,
        DateTimeOffset d => d.ToString("O"),
        DateOnly d => d.ToString("yyyy-MM-dd"),
        Enum e => e.ToString(),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(value),
    };
}
