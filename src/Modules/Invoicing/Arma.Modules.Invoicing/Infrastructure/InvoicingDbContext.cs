using Arma.Infrastructure.Auditing;
using Arma.Modules.Invoicing.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Invoicing.Infrastructure;

public sealed class InvoicingDbContext(DbContextOptions<InvoicingDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "invoicing";

    private readonly Guid tenantId = tenant.TenantId;

    public Guid TenantId => tenantId;

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceSettings> Settings => Set<InvoiceSettings>();

    /// <summary>Ön ek ve yıl için sıradaki fatura sıra numarası; eşzamanlı isteklerde de tekrarlanmaz.</summary>
    public async Task<long> NextNumberAsync(string prefix, int year, CancellationToken cancellationToken)
    {
        var rows = await Database.SqlQuery<long>($"""
            INSERT INTO invoicing.invoice_sequences (tenant_id, prefix, year, last_number) VALUES ({tenantId}, {prefix}, {year}, 1)
            ON CONFLICT (tenant_id, prefix, year) DO UPDATE SET last_number = invoice_sequences.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);
        return rows[0];
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("invoices");
            entity.Property(i => i.Number).HasMaxLength(16);
            entity.Property(i => i.Profile).HasConversion<short>();
            entity.Property(i => i.Status).HasConversion<short>();
            entity.Property(i => i.SourceKind).HasMaxLength(40);
            entity.Property(i => i.SourceLabel).HasMaxLength(200);
            entity.Property(i => i.BuyerName).HasMaxLength(200);
            entity.Property(i => i.BuyerFirstName).HasMaxLength(100);
            entity.Property(i => i.BuyerLastName).HasMaxLength(100);
            entity.Property(i => i.BuyerTaxId).HasMaxLength(11);
            entity.Property(i => i.BuyerTaxOffice).HasMaxLength(100);
            entity.Property(i => i.BuyerStreet).HasMaxLength(500);
            entity.Property(i => i.BuyerDistrict).HasMaxLength(100);
            entity.Property(i => i.BuyerCity).HasMaxLength(100);
            entity.Property(i => i.BuyerPostalCode).HasMaxLength(10);
            entity.Property(i => i.BuyerEmail).HasMaxLength(320);
            entity.Property(i => i.BuyerPhone).HasMaxLength(40);
            entity.Property(i => i.Note).HasMaxLength(2000);
            entity.Property(i => i.Provider).HasMaxLength(40);
            entity.Property(i => i.ProviderReference).HasMaxLength(200);
            entity.Property(i => i.LastError).HasMaxLength(1000);
            entity.Property(i => i.CancelReason).HasMaxLength(500);
            entity.Ignore(i => i.Subtotal);
            entity.Ignore(i => i.VatTotal);
            entity.Ignore(i => i.Total);
            entity.Ignore(i => i.Buyer);
            entity.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(i => i.Lines).AutoInclude();
            entity.HasIndex(i => new { i.TenantId, i.Number }).IsUnique().HasFilter("number IS NOT NULL");
            entity.HasIndex(i => new { i.TenantId, i.SourceKind, i.SourceId }).IsUnique().HasFilter("source_id IS NOT NULL");
            entity.HasIndex(i => new { i.TenantId, i.ContactId });
            entity.HasIndex(i => new { i.TenantId, i.Status, i.IssuedOn });
            entity.HasQueryFilter(i => i.TenantId == tenantId && !i.IsDeleted);
        });

        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.ToTable("invoice_lines");
            entity.Property(l => l.Id).ValueGeneratedNever();
            entity.Property(l => l.Description).HasMaxLength(500);
            entity.Property(l => l.Unit).HasMaxLength(20);
            entity.Property(l => l.Quantity).HasPrecision(18, 3);
            entity.Property(l => l.UnitPrice).HasPrecision(18, 2);
            entity.Property(l => l.DiscountRate).HasPrecision(5, 2);
            entity.Ignore(l => l.Net);
            entity.Ignore(l => l.Vat);
            entity.Ignore(l => l.AsDocumentLine);
            entity.HasQueryFilter(l => l.TenantId == tenantId && !l.IsDeleted);
        });

        modelBuilder.Entity<InvoiceSettings>(entity =>
        {
            entity.ToTable("invoice_settings");
            entity.Property(s => s.Prefix).HasMaxLength(3);
            entity.Property(s => s.SellerName).HasMaxLength(200);
            entity.Property(s => s.SellerTaxId).HasMaxLength(11);
            entity.Property(s => s.SellerTaxOffice).HasMaxLength(100);
            entity.Property(s => s.SellerStreet).HasMaxLength(500);
            entity.Property(s => s.SellerDistrict).HasMaxLength(100);
            entity.Property(s => s.SellerCity).HasMaxLength(100);
            entity.Property(s => s.SellerPostalCode).HasMaxLength(10);
            entity.Property(s => s.SellerEmail).HasMaxLength(320);
            entity.Property(s => s.SellerPhone).HasMaxLength(40);
            entity.Property(s => s.SellerWebsite).HasMaxLength(200);
            entity.Property(s => s.Provider).HasMaxLength(40);
            entity.Property(s => s.ExemptionCode).HasMaxLength(10);
            entity.Property(s => s.ExemptionReason).HasMaxLength(200);
            entity.Ignore(s => s.IsSellerComplete);
            entity.Ignore(s => s.Seller);
            entity.HasIndex(s => s.TenantId).IsUnique();
            entity.HasQueryFilter(s => s.TenantId == tenantId && !s.IsDeleted);
        });
    }
}
