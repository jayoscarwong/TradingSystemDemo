using Microsoft.EntityFrameworkCore;
using TradingSystem.Domain.Entities;

namespace TradingSystem.Infrastructure.Data
{
    public class TradingDbContext : DbContext
    {
        public TradingDbContext(DbContextOptions<TradingDbContext> options) : base(options) { }

        public DbSet<TradeOrder> TradeOrders { get; set; }
        public DbSet<TradingServer> TradingServers { get; set; }
        public DbSet<StockPrice> StockPrices { get; set; }
        public DbSet<JobExecutionHistory> JobExecutionHistories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TradeOrder>(entity =>
            {
                entity.HasKey(order => order.Id);

                entity.Property(order => order.Id)
                    .ValueGeneratedNever();

                entity.Property(order => order.BidAmount)
                    .HasPrecision(18, 4);

                entity.Property(order => order.Volume)
                    .HasPrecision(18, 4);

                entity.Property(order => order.ExecutedVolume)
                    .HasPrecision(18, 4);

                entity.Property(order => order.QueuedVolume)
                    .HasPrecision(18, 4);

                entity.Property(order => order.Status)
                    .HasMaxLength(50);

                entity.Property(order => order.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

                entity.Property(order => order.RowVersion)
                    .HasColumnType("timestamp(6)")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            });

            modelBuilder.Entity<StockPrice>(entity =>
            {
                entity.HasKey(stock => stock.Ticker);

                entity.Property(stock => stock.CurrentPrice)
                    .HasPrecision(18, 4);

                entity.Property(stock => stock.TotalStockVolume)
                    .HasPrecision(18, 4);

                entity.Property(stock => stock.AvailableVolume)
                    .HasPrecision(18, 4);

                entity.Property(stock => stock.BuyVolume)
                    .HasPrecision(18, 4);

                entity.Property(stock => stock.SellVolume)
                    .HasPrecision(18, 4);

                entity.Property(stock => stock.PendingBuyVolume)
                    .HasPrecision(18, 4);

                entity.Property(stock => stock.PendingSellVolume)
                    .HasPrecision(18, 4);

                entity.Property(stock => stock.LastUpdatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

                entity.Property(stock => stock.RowVersion)
                    .HasColumnType("timestamp(6)")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();
            });
        }
    }
}
