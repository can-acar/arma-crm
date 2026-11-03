using System.Reflection;
using Arma.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Arma.Infrastructure.Persistence;

/// <summary>
/// <see cref="BlindIndexAttribute"/> taşıyan alanları, kaynak hassas alanın anahtarlı özetiyle doldurur.
/// Hassas alan değiştiğinde özet de güncellenir; alan boşaltılırsa özet de boşalır.
/// </summary>
public sealed class BlindIndexInterceptor(ISensitiveDataProtector protector) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            foreach (var property in entry.Properties)
            {
                if (property.Metadata.PropertyInfo?.GetCustomAttribute<BlindIndexAttribute>() is not { } index)
                {
                    continue;
                }

                var source = entry.Property(index.SourceProperty);
                if (entry.State == EntityState.Added || source.IsModified)
                {
                    property.CurrentValue = source.CurrentValue is string { Length: > 0 } value ? protector.Hash(value) : null;
                }
            }
        }
    }
}
