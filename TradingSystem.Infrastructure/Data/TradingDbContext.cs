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
            modelBuilder.Entity<TradeOrder>()
               .Property(p => p.RowVersion)
               .IsRowVersion();

            modelBuilder.Entity<StockPrice>()
                .HasKey(sp => new { sp.Ticker, sp.ServerId }); // Composite Key
        }
    }
}