using Arma.Infrastructure.Auditing;
using Arma.Modules.Sales.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Sales.Infrastructure;

public sealed class SalesDbContext(DbContextOptions<SalesDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "sales";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<Pipeline> Pipelines => Set<Pipeline>();

    public DbSet<Stage> Stages => Set<Stage>();

    public DbSet<Opportunity> Opportunities => Set<Opportunity>();

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<QuoteLine> QuoteLines => Set<QuoteLine>();

    public DbSet<QuoteSettings> QuoteSettings => Set<QuoteSettings>();

    /// <summary>Yıl içinde artan teklif sırası; satır kilidiyle artırılır.</summary>
    public async Task<long> NextQuoteNumberAsync(int year, CancellationToken cancellationToken)
    {
        var rows = await Database.SqlQuery<long>($"""
            INSERT INTO sales.quote_sequences (tenant_id, year, last_number) VALUES ({tenantId}, {year}, 1)
            ON CONFLICT (tenant_id, year) DO UPDATE SET last_number = quote_sequences.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);
        return rows[0];
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Pipeline>(entity =>
        {
            entity.ToTable("pipelines");
            entity.Property(p => p.Name).HasMaxLength(100);
            entity.HasMany(p => p.Stages).WithOne().HasForeignKey(s => s.PipelineId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(p => p.Stages).AutoInclude();
            entity.HasQueryFilter(p => p.TenantId == tenantId && !p.IsDeleted);
        });

        modelBuilder.Entity<Stage>(entity =>
        {
            entity.ToTable("stages");
            entity.Property(s => s.Id).ValueGeneratedNever();
            entity.Property(s => s.Name).HasMaxLength(60);
            entity.Property(s => s.Kind).HasConversion<short>();
            entity.HasQueryFilter(s => s.TenantId == tenantId && !s.IsDeleted);
        });

        modelBuilder.Entity<Opportunity>(entity =>
        {
            entity.ToTable("opportunities");
            entity.Property(o => o.Title).HasMaxLength(200);
            entity.Property(o => o.Amount).HasPrecision(18, 2);
            entity.Property(o => o.Note).HasMaxLength(2000);
            entity.Property(o => o.LostReason).HasMaxLength(300);
            entity.HasOne<Pipeline>().WithMany().HasForeignKey(o => o.PipelineId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Stage>().WithMany().HasForeignKey(o => o.StageId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(o => new { o.TenantId, o.ContactId });
            entity.HasIndex(o => new { o.PipelineId, o.StageId, o.SortKey });
            entity.HasQueryFilter(o => o.TenantId == tenantId && !o.IsDeleted);
        });

        modelBuilder.Entity<Quote>(entity =>
        {
            entity.ToTable("quotes");
            entity.Property(q => q.Number).HasMaxLength(20);
            entity.Property(q => q.Status).HasConversion<short>();
            entity.Property(q => q.Terms).HasMaxLength(4000);
            entity.Ignore(q => q.Subtotal);
            entity.Ignore(q => q.VatTotal);
            entity.Ignore(q => q.Total);
            entity.HasMany(q => q.Lines).WithOne().HasForeignKey(l => l.QuoteId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(q => q.Lines).AutoInclude();
            entity.HasOne<Opportunity>().WithMany().HasForeignKey(q => q.OpportunityId).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(q => new { q.TenantId, q.Number }).IsUnique();
            entity.HasIndex(q => new { q.TenantId, q.ContactId });
            entity.HasQueryFilter(q => q.TenantId == tenantId && !q.IsDeleted);
        });

        modelBuilder.Entity<QuoteSettings>(entity =>
        {
            entity.ToTable("quote_settings");
            entity.Property(s => s.OrganizationName).HasMaxLength(200);
            entity.Property(s => s.Address).HasMaxLength(500);
            entity.Property(s => s.LegalInfo).HasMaxLength(500);
            entity.Property(s => s.DefaultTerms).HasMaxLength(4000);
            entity.HasIndex(s => s.TenantId).IsUnique();
            entity.HasQueryFilter(s => s.TenantId == tenantId && !s.IsDeleted);
        });

        modelBuilder.Entity<QuoteLine>(entity =>
        {
            entity.ToTable("quote_lines");
            entity.Property(l => l.Id).ValueGeneratedNever();
            entity.Property(l => l.Description).HasMaxLength(500);
            entity.Property(l => l.Unit).HasMaxLength(20);
            entity.Property(l => l.Quantity).HasPrecision(18, 3);
            entity.Property(l => l.UnitPrice).HasPrecision(18, 2);
            entity.Property(l => l.DiscountRate).HasPrecision(5, 2);
            entity.Ignore(l => l.Net);
            entity.Ignore(l => l.Vat);
            entity.HasQueryFilter(l => l.TenantId == tenantId && !l.IsDeleted);
        });
    }
}
