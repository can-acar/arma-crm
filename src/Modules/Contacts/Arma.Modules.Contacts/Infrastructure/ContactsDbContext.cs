using Arma.Infrastructure.Auditing;
using Arma.Modules.Contacts.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Infrastructure;

public sealed class ContactsDbContext(DbContextOptions<ContactsDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "contacts";

    /// <summary>Row Level Security uygulanan tablolar (migration'larda kullanılır).</summary>
    public static readonly string[] TenantTables = ["contacts", "tags", "groups", "relationships", "saved_searches"];

    private readonly Guid tenantId = tenant.TenantId;

    internal Guid TenantId => tenantId;

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<Relationship> Relationships => Set<Relationship>();

    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();

    public DbSet<CustomField> CustomFields => Set<CustomField>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Type).HasConversion<short>();
            entity.Property(c => c.FirstName).HasMaxLength(100);
            entity.Property(c => c.LastName).HasMaxLength(100);
            entity.Property(c => c.OrganizationName).HasMaxLength(200);
            entity.Property(c => c.DisplayName).HasMaxLength(200).IsRequired().UseCollation("tr-TR-x-icu");
            entity.Property(c => c.Email).HasMaxLength(254);
            entity.Property(c => c.Phone).HasMaxLength(32);
            entity.Property(c => c.NationalId).HasMaxLength(11);
            entity.Property(c => c.Website).HasMaxLength(300);
            entity.Property(c => c.SearchText).IsRequired();
            entity.Property(c => c.CustomFieldsJson).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
            entity.Ignore(c => c.CustomFields);
            entity.Ignore(c => c.MaskedNationalId);
            entity.Ignore(c => c.PrimaryAddress);

            entity.OwnsMany(c => c.Addresses, address =>
            {
                address.ToJson("addresses");
                address.Property(a => a.Kind).HasConversion<string>();
            });

            entity.HasMany(c => c.Tags).WithMany(t => t.Contacts).UsingEntity("contact_tags");
            entity.HasMany(c => c.Groups).WithMany(g => g.Members).UsingEntity("group_members");

            entity.HasIndex(c => new { c.TenantId, c.DisplayName });
            entity.HasIndex(c => new { c.TenantId, c.Email });
            entity.HasIndex(c => new { c.TenantId, c.NationalId }).IsUnique().HasFilter("national_id IS NOT NULL AND NOT is_deleted");
            entity.HasIndex(c => c.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops");

            entity.HasQueryFilter(c => c.TenantId == tenantId && !c.IsDeleted);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(60).UseCollation("tr-TR-x-icu");
            entity.Property(t => t.Color).HasMaxLength(20);
            entity.HasIndex(t => new { t.TenantId, t.Name }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(t => t.TenantId == tenantId && !t.IsDeleted);
        });

        modelBuilder.Entity<Group>(entity =>
        {
            entity.Property(g => g.Name).HasMaxLength(120).UseCollation("tr-TR-x-icu");
            entity.Property(g => g.Description).HasMaxLength(500);
            entity.HasIndex(g => new { g.TenantId, g.Name }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(g => g.TenantId == tenantId && !g.IsDeleted);
        });

        modelBuilder.Entity<Relationship>(entity =>
        {
            entity.Property(r => r.Type).HasConversion<short>();
            entity.Property(r => r.Description).HasMaxLength(300);
            entity.Ignore(r => r.IsActive);
            entity.HasOne<Contact>().WithMany().HasForeignKey(r => r.FromContactId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Contact>().WithMany().HasForeignKey(r => r.ToContactId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(r => r.FromContactId);
            entity.HasIndex(r => r.ToContactId);
            entity.HasQueryFilter(r => r.TenantId == tenantId && !r.IsDeleted);
        });

        modelBuilder.Entity<SavedSearch>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(120).UseCollation("tr-TR-x-icu");
            entity.Property(s => s.Criteria).HasColumnType("jsonb");
            entity.HasIndex(s => new { s.TenantId, s.Name }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(s => s.TenantId == tenantId && !s.IsDeleted);
        });

        modelBuilder.Entity<CustomField>(entity =>
        {
            entity.ToTable("custom_fields");
            entity.Property(f => f.Label).HasMaxLength(100).UseCollation("tr-TR-x-icu");
            entity.Property(f => f.Type).HasConversion<short>();
            entity.Property(f => f.AppliesTo).HasConversion<short?>();
            entity.Property(f => f.OptionsText).HasMaxLength(5000);
            entity.Property(f => f.HelpText).HasMaxLength(300);
            entity.Ignore(f => f.Options);
            entity.HasIndex(f => new { f.TenantId, f.Label }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(f => f.TenantId == tenantId && !f.IsDeleted);
        });
    }
}
