using Microsoft.EntityFrameworkCore;
using PayBridge.Shared.Domain;

namespace PayBridge.PaymentApi.Persistence;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

    public DbSet<Payment> Payments => Set<Payment>();
    // Tracked here so the API owns DDL for the shared database; the consumer reads/writes the
    // same table via its own SettlementDbContext.
    public DbSet<SettlementRecord> Settlements => Set<SettlementRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var p = modelBuilder.Entity<Payment>();
        p.ToTable("payments");
        p.HasKey(x => x.Id);

        p.Property(x => x.MerchantId).HasMaxLength(64).IsRequired();
        p.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
        p.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        p.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        p.Property(x => x.Amount).HasPrecision(18, 4);
        p.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
        p.Property(x => x.Method).HasConversion<string>().HasMaxLength(32);
        p.Property(x => x.ProviderTransactionId).HasMaxLength(128);
        p.Property(x => x.FailureReason).HasMaxLength(512);
        p.Property(x => x.OriginatingTraceId).HasMaxLength(64);

        // The (merchant_id, idempotency_key) tuple uniquely identifies a payment attempt.
        // We store payments anyway so the API can return the original result on retries.
        p.HasIndex(x => new { x.MerchantId, x.IdempotencyKey }).IsUnique();
        p.HasIndex(x => x.ProviderTransactionId);

        var s = modelBuilder.Entity<SettlementRecord>();
        s.ToTable("settlements");
        s.HasKey(x => x.Id);
        s.Property(x => x.Id).ValueGeneratedOnAdd();
        s.Property(x => x.MerchantId).HasMaxLength(64).IsRequired();
        s.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
        s.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        s.Property(x => x.Amount).HasPrecision(18, 4);
        s.Property(x => x.FinalStatus).HasConversion<string>().HasMaxLength(32);
        s.Property(x => x.ProviderTransactionId).HasMaxLength(128);
        s.HasIndex(x => new { x.PaymentId, x.FinalStatus }).IsUnique();
    }
}
