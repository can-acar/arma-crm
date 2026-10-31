using Arma.Infrastructure.Auditing;
using Arma.Modules.Activities.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Activities.Infrastructure;

public sealed class ActivitiesDbContext(DbContextOptions<ActivitiesDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "activities";

    /// <summary>Row Level Security uygulanan tablolar (migration'larda kullanılır).</summary>
    public static readonly string[] TenantTables = ["activities", "activity_participants"];

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<Activity> Activities => Set<Activity>();

    public DbSet<ActivityParticipant> Participants => Set<ActivityParticipant>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Activity>(entity =>
        {
            entity.Property(a => a.Kind).HasConversion<short>();
            entity.Property(a => a.Status).HasConversion<short>();
            entity.Property(a => a.Subject).HasMaxLength(Activity.SubjectMaxLength).IsRequired();
            entity.Property(a => a.Body).HasMaxLength(Activity.BodyMaxLength);
            entity.Property(a => a.SourceModule).HasMaxLength(50);
            entity.Property(a => a.SourceId).HasMaxLength(100);
            entity.Ignore(a => a.IsTask);
            entity.Ignore(a => a.IsSystem);

            entity.HasMany(a => a.Participants).WithOne().HasForeignKey(p => p.ActivityId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(a => a.Participants).HasField("participants").UsePropertyAccessMode(PropertyAccessMode.Field);

            entity.HasIndex(a => new { a.TenantId, a.AssignedToUserId, a.Status });
            entity.HasIndex(a => new { a.TenantId, a.SourceModule, a.SourceId }).IsUnique().HasFilter("source_module IS NOT NULL AND NOT is_deleted");
            entity.HasQueryFilter(a => a.TenantId == tenantId && !a.IsDeleted);
        });

        modelBuilder.Entity<ActivityParticipant>(entity =>
        {
            entity.ToTable("activity_participants");

            // Kimlik uygulamada üretilir; aktiviteye sonradan eklenen katılımcı güncelleme değil ekleme sayılsın.
            entity.Property(p => p.Id).ValueGeneratedNever();
            entity.HasIndex(p => p.ContactId);
            entity.HasIndex(p => new { p.ActivityId, p.ContactId }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(p => p.TenantId == tenantId && !p.IsDeleted);
        });
    }
}
