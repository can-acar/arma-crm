using Arma.Modules.Contacts.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Contacts.Infrastructure;

public sealed class ContactsDbContext(DbContextOptions<ContactsDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "contacts";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("pg_trgm");

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
            entity.Property(c => c.SearchText).IsRequired();
            entity.Ignore(c => c.MaskedNationalId);

            entity.HasIndex(c => new { c.TenantId, c.DisplayName });
            entity.HasIndex(c => new { c.TenantId, c.Email });
            entity.HasIndex(c => c.SearchText).HasMethod("gin").HasOperators("gin_trgm_ops");

            entity.HasQueryFilter(c => c.TenantId == tenantId && !c.IsDeleted);
        });
    }
}
