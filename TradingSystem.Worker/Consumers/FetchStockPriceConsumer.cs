using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TradingSystem.Application.Commands;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Worker.Consumers
{
    public class FetchStockPriceConsumer : IConsumer<FetchStockPriceCommand>
    {
        private static readonly DistributedCacheEntryOptions PriceCacheOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        };

        private readonly ILogger<FetchStockPriceConsumer> _logger;
        private readonly TradingDbContext _dbContext;
        private readonly IDistributedCache _redisCache;

        public FetchStockPriceConsumer(
            ILogger<FetchStockPriceConsumer> logger,
            TradingDbContext dbContext,
            IDistributedCache redisCache)
        {
            _logger = logger;
            _dbContext = dbContext;
            _redisCache = redisCache;
        }

        public async Task Consume(ConsumeContext<FetchStockPriceCommand> context)
        {
            var command = context.Message;
            var normalizedTicker = command.Ticker.Trim().ToUpperInvariant();
            var nowUtc = DateTime.UtcNow;

            var stock = await _dbContext.StockPrices
                .AsNoTracking()
                .SingleOrDefaultAsync(price => price.Ticker == normalizedTicker, context.CancellationToken);

            if (stock == null)
            {
                _logger.LogWarning(
                    "Server {ServerId} requested market data for unknown ticker {Ticker}.",
                    command.ServerId,
                    normalizedTicker);
                return;
            }

            var tradingServer = await _dbContext.TradingServers
                .SingleOrDefaultAsync(server => server.Id == command.ServerId, context.CancellationToken);

            if (tradingServer != null)
            {
                tradingServer.LastPingAt = nowUtc;
                await _dbContext.SaveChangesAsync(context.CancellationToken);
            }

            await _redisCache.SetStringAsync(
                GetPriceCacheKey(normalizedTicker),
                JsonSerializer.Serialize(stock),
                PriceCacheOptions,
                context.CancellationToken);

            _logger.LogInformation(
                "[Market Poll] Server {ServerId} refreshed {Ticker} at {Price:F4}. Available {AvailableVolume:F4}, queued buy {PendingBuyVolume:F4}, queued sell {PendingSellVolume:F4}.",
                command.ServerId,
                normalizedTicker,
                stock.CurrentPrice,
                stock.AvailableVolume,
                stock.PendingBuyVolume,
                stock.PendingSellVolume);
        }

        private static string GetPriceCacheKey(string ticker) => $"price_{ticker}";
    }
}
