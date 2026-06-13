using Microsoft.EntityFrameworkCore;
using PayBridge.Shared.Domain;

namespace PayBridge.SettlementConsumer.Persistence;

public class SettlementDbContext : DbContext
{
    public SettlementDbContext(DbContextOptions<SettlementDbContext> options) : base(options) { }

    public DbSet<SettlementRecord> Settlements => Set<SettlementRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

        // Same payment + same final status from a duplicate message is a no-op.
        s.HasIndex(x => new { x.PaymentId, x.FinalStatus }).IsUnique();
    }
}
