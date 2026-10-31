using Arma.Infrastructure.Auditing;
using Arma.Modules.Events.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Events.Infrastructure;

public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "events";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<Event> Events => Set<Event>();

    public DbSet<TicketType> TicketTypes => Set<TicketType>();

    public DbSet<Registration> Registrations => Set<Registration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("events");
            entity.Property(e => e.Title).HasMaxLength(200);
            entity.Property(e => e.Slug).HasMaxLength(80);
            entity.Property(e => e.Description).HasMaxLength(5000);
            entity.Property(e => e.Location).HasMaxLength(300);
            entity.Property(e => e.ConfirmationNote).HasMaxLength(1000);
            entity.Property(e => e.Status).HasConversion<short>();
            entity.HasIndex(e => new { e.TenantId, e.Slug }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasIndex(e => new { e.TenantId, e.StartsAt });
            entity.HasQueryFilter(e => e.TenantId == tenantId && !e.IsDeleted);
        });

        modelBuilder.Entity<TicketType>(entity =>
        {
            entity.ToTable("ticket_types");
            entity.Property(t => t.Name).HasMaxLength(100);
            entity.Property(t => t.Price).HasPrecision(18, 2);
            entity.HasOne<Event>().WithMany().HasForeignKey(t => t.EventId).OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(t => t.TenantId == tenantId && !t.IsDeleted);
        });

        modelBuilder.Entity<Registration>(entity =>
        {
            entity.ToTable("registrations");
            entity.Property(r => r.Status).HasConversion<short>();
            entity.Property(r => r.Payment).HasConversion<short>();
            entity.Property(r => r.Amount).HasPrecision(18, 2);
            entity.Property(r => r.Code).HasMaxLength(10);
            entity.Property(r => r.Source).HasMaxLength(10);
            entity.Property(r => r.Note).HasMaxLength(500);
            entity.HasOne<Event>().WithMany().HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<TicketType>().WithMany().HasForeignKey(r => r.TicketTypeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(r => new { r.TenantId, r.Code }).IsUnique();
            entity.HasIndex(r => new { r.EventId, r.Status });
            entity.HasIndex(r => new { r.TenantId, r.ContactId });
            entity.HasQueryFilter(r => r.TenantId == tenantId && !r.IsDeleted);
        });
    }
}
