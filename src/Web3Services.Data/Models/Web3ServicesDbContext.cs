using Argus.Sync.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Web3Services.Data.Models.Entity;

namespace Web3Services.Data.Models;

public class Web3ServicesDbContext(
    DbContextOptions<Web3ServicesDbContext> options,
    IConfiguration configuration
) : CardanoDbContext(options, configuration)
{
    public DbSet<OutputBySlot> OutputsBySlot => Set<OutputBySlot>();
    public DbSet<TransactionByAddress> TransactionsByAddress => Set<TransactionByAddress>();
    public DbSet<TrackedAddress> TrackedAddresses => Set<TrackedAddress>();
    public DbSet<BlockTest> BlockTests => Set<BlockTest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<OutputBySlot>(entity =>
        {
            entity.HasKey(e => e.OutRef);

            entity.HasIndex(e => e.PaymentKeyHash);
            entity.HasIndex(e => e.StakeKeyHash);
            entity.HasIndex(e => new { e.PaymentKeyHash, e.StakeKeyHash });
            entity.HasIndex(e => e.Slot);
            entity.HasIndex(e => new { e.PaymentKeyHash, e.Slot });
            entity.HasIndex(e => new { e.StakeKeyHash, e.Slot });
            entity.HasIndex(e => e.SpentSlot);
            entity.HasIndex(e => e.SpentTxHash);
        });

        modelBuilder.Entity<TransactionByAddress>(entity =>
        {
            entity.HasKey(e => new { e.PaymentKeyHash, e.StakeKeyHash, e.Hash });

            entity.HasIndex(e => e.PaymentKeyHash);
            entity.HasIndex(e => new { e.PaymentKeyHash, e.StakeKeyHash });

            entity.HasIndex(e => new { e.PaymentKeyHash, e.Slot, e.Hash })
                .IsDescending(false, true, true);
            entity.HasIndex(e => new { e.PaymentKeyHash, e.StakeKeyHash, e.Slot, e.Hash })
                .IsDescending(false, false, true, true);

            entity.HasIndex(e => e.Subjects)
                .HasMethod("gin");

            entity.HasIndex(e => e.Slot);
            entity.HasIndex(e => e.Hash);
        });

        modelBuilder.Entity<TrackedAddress>(entity =>
        {
            entity.HasKey(e => new { e.PaymentKeyHash, e.StakeKeyHash });
            entity.HasIndex(e => e.PaymentKeyHash);
            entity.HasIndex(e => e.StakeKeyHash);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<BlockTest>(entity =>
        {
            entity.HasKey(e => e.Hash);
        });
    }
}