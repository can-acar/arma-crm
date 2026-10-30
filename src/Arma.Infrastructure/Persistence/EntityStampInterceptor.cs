using Arma.SharedKernel.Domain;
using Arma.SharedKernel.Security;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Arma.Infrastructure.Persistence;

/// <summary>
/// Kaydetmeden önce <see cref="Entity"/> alanlarını doldurur: kurum, oluşturan/güncelleyen ve zamanlar.
/// Silme işlemi yumuşak silmeye çevrilir; satır veritabanında kalır.
/// </summary>
public sealed class EntityStampInterceptor(ITenantContext tenant, ICurrentUser user, TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.GetUtcNow();
        var who = user.Id;
        foreach (var entry in context.ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.TenantId = tenant.TenantId;
                    entry.Entity.CreatedAt = now;
                    entry.Entity.CreatedBy = who;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = who;
                    break;
                case EntityState.Deleted:
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = who;
                    break;
            }
        }
    }
}
