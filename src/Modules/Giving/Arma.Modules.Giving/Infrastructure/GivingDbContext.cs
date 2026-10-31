using Arma.Infrastructure.Auditing;
using Arma.Modules.Giving.Domain;
using Arma.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arma.Modules.Giving.Infrastructure;

public sealed class GivingDbContext(DbContextOptions<GivingDbContext> options, ITenantContext tenant)
    : DbContext(options)
{
    public const string Schema = "giving";

    private readonly Guid tenantId = tenant.TenantId;

    public DbSet<Donation> Donations => Set<Donation>();

    public DbSet<Fund> Funds => Set<Fund>();

    public DbSet<Pledge> Pledges => Set<Pledge>();

    public DbSet<ReceiptSettings> ReceiptSettings => Set<ReceiptSettings>();

    public DbSet<BankStatement> BankStatements => Set<BankStatement>();

    public DbSet<BankLine> BankLines => Set<BankLine>();

    /// <summary>
    /// Yıl içinde boşluksuz artan makbuz sırası. Satır kilidiyle artırılır; aynı anda kesilen iki makbuz
    /// aynı numarayı alamaz. Çağıran işlemin içinde çalışmalıdır.
    /// </summary>
    public async Task<long> NextReceiptNumberAsync(int year, CancellationToken cancellationToken)
    {
        var rows = await Database.SqlQuery<long>($"""
            INSERT INTO giving.receipt_sequences (tenant_id, year, last_number) VALUES ({tenantId}, {year}, 1)
            ON CONFLICT (tenant_id, year) DO UPDATE SET last_number = receipt_sequences.last_number + 1
            RETURNING last_number AS "Value"
            """).ToListAsync(cancellationToken);
        return rows[0];
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.MapAuditLog();

        modelBuilder.Entity<Donation>(entity =>
        {
            entity.Property(d => d.Amount).HasPrecision(18, 2);
            entity.Property(d => d.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(d => d.Method).HasConversion<short>();
            entity.Property(d => d.Status).HasConversion<short>();
            entity.Property(d => d.Reference).HasMaxLength(100);
            entity.Property(d => d.Note).HasMaxLength(1000);
            entity.Property(d => d.InKindDescription).HasMaxLength(500);
            entity.Property(d => d.ReceiptNumber).HasMaxLength(40);
            entity.Property(d => d.ReceiptVoidReason).HasMaxLength(300);
            entity.Property(d => d.Provider).HasMaxLength(30);
            entity.Property(d => d.ProviderPaymentId).HasMaxLength(100);
            entity.Property(d => d.ExternalKey).HasMaxLength(100);
            entity.HasIndex(d => new { d.TenantId, d.ExternalKey }).IsUnique().HasFilter("external_key IS NOT NULL AND NOT is_deleted");
            entity.Ignore(d => d.HasReceipt);
            entity.HasOne<Fund>().WithMany().HasForeignKey(d => d.FundId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(d => new { d.TenantId, d.ContactId });
            entity.HasIndex(d => new { d.TenantId, d.ReceivedOn });
            entity.HasOne<Pledge>().WithMany().HasForeignKey(d => d.PledgeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(d => new { d.TenantId, d.PledgeId }).HasFilter("pledge_id IS NOT NULL");
            entity.HasIndex(d => new { d.TenantId, d.ReceiptNumber }).IsUnique().HasFilter("receipt_number IS NOT NULL");
            entity.HasQueryFilter(d => d.TenantId == tenantId && !d.IsDeleted);
        });

        modelBuilder.Entity<Fund>(entity =>
        {
            entity.Property(f => f.Name).HasMaxLength(120).UseCollation("tr-TR-x-icu");
            entity.Property(f => f.Description).HasMaxLength(500);
            entity.HasIndex(f => new { f.TenantId, f.Name }).IsUnique().HasFilter("NOT is_deleted");
            entity.HasQueryFilter(f => f.TenantId == tenantId && !f.IsDeleted);
        });

        modelBuilder.Entity<Pledge>(entity =>
        {
            entity.Property(p => p.Amount).HasPrecision(18, 2);
            entity.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();
            entity.Property(p => p.Frequency).HasConversion<short>();
            entity.Property(p => p.Status).HasConversion<short>();
            entity.Property(p => p.Note).HasMaxLength(1000);
            entity.Property(p => p.CancelReason).HasMaxLength(300);
            entity.HasOne<Fund>().WithMany().HasForeignKey(p => p.FundId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(p => new { p.TenantId, p.ContactId });
            entity.HasIndex(p => new { p.TenantId, p.Status });
            entity.HasQueryFilter(p => p.TenantId == tenantId && !p.IsDeleted);
        });

        modelBuilder.Entity<BankStatement>(entity =>
        {
            entity.ToTable("bank_statements");
            entity.Property(s => s.FileName).HasMaxLength(200);
            entity.Property(s => s.Format).HasMaxLength(10);
            entity.HasQueryFilter(s => s.TenantId == tenantId && !s.IsDeleted);
        });

        modelBuilder.Entity<BankLine>(entity =>
        {
            entity.ToTable("bank_lines");
            entity.Property(l => l.Key).HasMaxLength(40);
            entity.Property(l => l.Amount).HasPrecision(18, 2);
            entity.Property(l => l.Description).HasMaxLength(500);
            entity.Property(l => l.Counterparty).HasMaxLength(200);
            entity.Property(l => l.Iban).HasMaxLength(34);
            entity.Property(l => l.BankReference).HasMaxLength(100);
            entity.Property(l => l.SuggestionReason).HasMaxLength(10);
            entity.Property(l => l.Status).HasConversion<short>();
            entity.HasOne<BankStatement>().WithMany().HasForeignKey(l => l.StatementId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(l => new { l.TenantId, l.Key }).IsUnique();
            entity.HasIndex(l => new { l.TenantId, l.Status, l.BookedOn });
            entity.HasIndex(l => new { l.TenantId, l.Iban });
            entity.HasIndex(l => new { l.TenantId, l.ContactId });
            entity.HasQueryFilter(l => l.TenantId == tenantId && !l.IsDeleted);
        });

        modelBuilder.Entity<ReceiptSettings>(entity =>
        {
            entity.ToTable("receipt_settings");
            entity.Property(s => s.OrganizationName).HasMaxLength(200);
            entity.Property(s => s.Address).HasMaxLength(500);
            entity.Property(s => s.LegalInfo).HasMaxLength(500);
            entity.Property(s => s.Footer).HasMaxLength(1000);
            entity.Property(s => s.NumberPrefix).HasMaxLength(10);
            entity.HasIndex(s => s.TenantId).IsUnique();
            entity.HasQueryFilter(s => s.TenantId == tenantId && !s.IsDeleted);
        });
    }
}
