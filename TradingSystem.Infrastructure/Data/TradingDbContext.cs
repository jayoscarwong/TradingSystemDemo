using Microsoft.EntityFrameworkCore;
using TradingSystem.Domain.Entities;

namespace TradingSystem.Infrastructure.Data
{
    public class TradingDbContext : DbContext
    {
        public TradingDbContext(DbContextOptions<TradingDbContext> options) : base(options) { }

        public DbSet<ScheduledTask> ScheduledTasks { get; set; }
        public DbSet<TradeAccount> TradeAccounts { get; set; }
        public DbSet<TradeUserGroup> TradeUserGroups { get; set; }
        public DbSet<TradePermission> TradePermissions { get; set; }
        public DbSet<TradeAccountGroup> TradeAccountGroups { get; set; }
        public DbSet<TradeGroupPermission> TradeGroupPermissions { get; set; }
        public DbSet<TradeRefreshToken> TradeRefreshTokens { get; set; }
        public DbSet<TradeOrder> TradeOrders { get; set; }
        public DbSet<TradingServer> TradingServers { get; set; }
        public DbSet<StockPrice> StockPrices { get; set; }
        public DbSet<JobExecutionHistory> JobExecutionHistories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ScheduledTask>(entity =>
            {
                entity.HasKey(task => task.Id);

                entity.Property(task => task.Name)
                    .HasMaxLength(200);

                entity.HasIndex(task => task.Name)
                    .IsUnique();

                entity.Property(task => task.Description)
                    .HasMaxLength(500);

                entity.Property(task => task.TaskType)
                    .HasMaxLength(50);

                entity.Property(task => task.ScheduleType)
                    .HasMaxLength(20);

                entity.Property(task => task.CronExpression)
                    .HasMaxLength(120);

                entity.Property(task => task.Ticker)
                    .HasMaxLength(10);

                entity.Property(task => task.RuntimeStatus)
                    .HasMaxLength(50);

                entity.Property(task => task.LastExecutionStatus)
                    .HasMaxLength(50);

                entity.Property(task => task.LastSchedulerInstance)
                    .HasMaxLength(200);

                entity.Property(task => task.LastExecutionDurationMs)
                    .HasPrecision(18, 4);

                entity.Property(task => task.AverageDurationMs)
                    .HasPrecision(18, 4);

                entity.Property(task => task.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

                entity.Property(task => task.UpdatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

                entity.Property(task => task.RowVersion)
                    .HasColumnType("timestamp(6)")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();

                entity.HasIndex(task => new { task.TaskType, task.IsEnabled, task.IsPaused });
                entity.HasIndex(task => task.NextFireTime);
                entity.HasIndex(task => new { task.ServerId, task.Ticker });

                entity.HasOne(task => task.TradingServer)
                    .WithMany()
                    .HasForeignKey(task => task.ServerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<TradeAccount>(entity =>
            {
                entity.HasKey(account => account.Id);

                entity.Property(account => account.Name)
                    .HasMaxLength(200);

                entity.Property(account => account.Username)
                    .HasMaxLength(100);

                entity.Property(account => account.Email)
                    .HasMaxLength(200);

                entity.Property(account => account.PasswordHash)
                    .HasColumnType("varbinary(64)");

                entity.Property(account => account.PasswordSalt)
                    .HasColumnType("varbinary(128)");

                entity.Property(account => account.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

                entity.Property(account => account.RowVersion)
                    .HasColumnType("timestamp(6)")
                    .ValueGeneratedOnAddOrUpdate()
                    .IsConcurrencyToken();

                entity.HasIndex(account => account.Username)
                    .IsUnique();

                entity.HasIndex(account => account.Email)
                    .IsUnique();
            });

            modelBuilder.Entity<TradeRefreshToken>(entity =>
            {
                entity.HasKey(token => token.Id);

                entity.Property(token => token.TokenHash)
                    .HasMaxLength(64);

                entity.Property(token => token.ReplacedByTokenHash)
                    .HasMaxLength(64);

                entity.Property(token => token.CreatedAt)
                    .HasDefaultValueSql("CURRENT_TIMESTAMP(6)");

                entity.HasIndex(token => token.TokenHash)
                    .IsUnique();

                entity.HasIndex(token => new { token.TradeAccountId, token.ExpiresAt });

                entity.HasOne(token => token.TradeAccount)
                    .WithMany(account => account.RefreshTokens)
                    .HasForeignKey(token => token.TradeAccountId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TradeUserGroup>(entity =>
            {
                entity.HasKey(group => group.Id);

                entity.Property(group => group.Name)
                    .HasMaxLength(100);

                entity.Property(group => group.Description)
                    .HasMaxLength(300);

                entity.HasIndex(group => group.Name)
                    .IsUnique();
            });

            modelBuilder.Entity<TradePermission>(entity =>
            {
                entity.HasKey(permission => permission.Id);

                entity.Property(permission => permission.Code)
                    .HasMaxLength(100);

                entity.Property(permission => permission.Description)
                    .HasMaxLength(300);

                entity.HasIndex(permission => permission.Code)
                    .IsUnique();
            });

            modelBuilder.Entity<TradeAccountGroup>(entity =>
            {
                entity.HasKey(link => new { link.TradeAccountId, link.TradeUserGroupId });

                entity.HasOne(link => link.TradeAccount)
                    .WithMany(account => account.AccountGroups)
                    .HasForeignKey(link => link.TradeAccountId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(link => link.TradeUserGroup)
                    .WithMany(group => group.AccountGroups)
                    .HasForeignKey(link => link.TradeUserGroupId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TradeGroupPermission>(entity =>
            {
                entity.HasKey(link => new { link.TradeUserGroupId, link.TradePermissionId });

                entity.HasOne(link => link.TradeUserGroup)
                    .WithMany(group => group.GroupPermissions)
                    .HasForeignKey(link => link.TradeUserGroupId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(link => link.TradePermission)
                    .WithMany(permission => permission.GroupPermissions)
                    .HasForeignKey(link => link.TradePermissionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TradeOrder>(entity =>
            {
                entity.HasKey(order => order.Id);

                entity.Property(order => order.Id)
                    .ValueGeneratedNever();

                entity.HasIndex(order => new { order.TradeAccountId, order.CreatedAt });

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

                entity.HasOne(order => order.TradeAccount)
                    .WithMany(account => account.TradeOrders)
                    .HasForeignKey(order => order.TradeAccountId)
                    .OnDelete(DeleteBehavior.Restrict);
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

            modelBuilder.Entity<JobExecutionHistory>(entity =>
            {
                entity.HasKey(history => history.Id);

                entity.Property(history => history.Id)
                    .ValueGeneratedNever();

                entity.Property(history => history.JobName)
                    .HasMaxLength(200);

                entity.Property(history => history.TaskName)
                    .HasMaxLength(200);

                entity.Property(history => history.TaskType)
                    .HasMaxLength(50);

                entity.Property(history => history.Ticker)
                    .HasMaxLength(10);

                entity.Property(history => history.Status)
                    .HasMaxLength(50);

                entity.Property(history => history.SchedulerInstance)
                    .HasMaxLength(200);

                entity.HasIndex(history => new { history.ScheduledTaskId, history.StartTime });
                entity.HasIndex(history => new { history.JobName, history.StartTime });

                entity.HasOne(history => history.ScheduledTask)
                    .WithMany(task => task.ExecutionHistories)
                    .HasForeignKey(history => history.ScheduledTaskId)
                    .OnDelete(DeleteBehavior.SetNull);
            });
        }
    }
}
