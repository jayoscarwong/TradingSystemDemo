using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using TradingSystem.Application.Commands;
using TradingSystem.Infrastructure.Data;
using TradingSystem.Worker.Services;

namespace TradingSystem.Worker.Consumers
{
    public class ProcessTradeConsumer : IConsumer<ProcessTradeCommand>
    {
        private static readonly DistributedCacheEntryOptions PriceCacheOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        };

        private readonly ILogger<ProcessTradeConsumer> _logger;
        private readonly TradingDbContext _dbContext;
        private readonly IDistributedCache _redisCache;
        private readonly TradeExecutionService _tradeExecutionService;

        public ProcessTradeConsumer(
            ILogger<ProcessTradeConsumer> logger,
            TradingDbContext dbContext,
            IDistributedCache redisCache,
            TradeExecutionService tradeExecutionService)
        {
            _logger = logger;
            _dbContext = dbContext;
            _redisCache = redisCache;
            _tradeExecutionService = tradeExecutionService;
        }

        public async Task Consume(ConsumeContext<ProcessTradeCommand> context)
        {
            var command = context.Message;

            while (true)
            {
                try
                {
                    _dbContext.ChangeTracker.Clear();

                    var order = await _dbContext.TradeOrders
                        .SingleOrDefaultAsync(tradeOrder => tradeOrder.Id == command.OrderId, context.CancellationToken);

                    if (order == null)
                    {
                        _logger.LogWarning("Trade order {OrderId} was not found.", command.OrderId);
                        return;
                    }

                    if (order.IsProcessed)
                    {
                        _logger.LogInformation("Trade order {OrderId} was already processed. Skipping duplicate worker execution.", command.OrderId);
                        return;
                    }

                    var stock = await _dbContext.StockPrices
                        .SingleOrDefaultAsync(stockPrice => stockPrice.Ticker == order.StockTicker, context.CancellationToken);

                    if (stock == null)
                    {
                        order.Status = "Rejected";
                        order.ExecutedVolume = 0m;
                        order.QueuedVolume = order.Volume;
                        order.ProcessedAt = DateTime.UtcNow;
                        order.IsProcessed = true;
                        await _dbContext.SaveChangesAsync(context.CancellationToken);

                        _logger.LogWarning("Trade order {OrderId} references unknown ticker {Ticker}. Order marked as rejected.", order.Id, order.StockTicker);
                        return;
                    }

                    var summary = _tradeExecutionService.Apply(order, stock, DateTime.UtcNow);

                    await _dbContext.SaveChangesAsync(context.CancellationToken);

                    await _redisCache.SetStringAsync(
                        GetPriceCacheKey(stock.Ticker),
                        JsonSerializer.Serialize(stock),
                        PriceCacheOptions,
                        context.CancellationToken);

                    _logger.LogInformation(
                        "[Market] {Ticker} moved from {PreviousPrice:F4} to {NewPrice:F4}. Executed {ExecutedVolume:F4}, queued {QueuedVolume:F4}, available {AvailableVolume:F4}, pending buy {PendingBuyVolume:F4}, pending sell {PendingSellVolume:F4}.",
                        stock.Ticker,
                        summary.PreviousPrice,
                        summary.NewPrice,
                        summary.ExecutedVolume,
                        summary.QueuedVolume,
                        summary.AvailableVolume,
                        summary.PendingBuyVolume,
                        summary.PendingSellVolume);

                    return;
                }
                catch (DbUpdateConcurrencyException)
                {
                    _logger.LogWarning(
                        "Optimistic concurrency collision while processing order {OrderId}. Reloading the latest row versions and retrying.",
                        command.OrderId);

                    await Task.Delay(25, context.CancellationToken);
                }
            }
        }

        private static string GetPriceCacheKey(string ticker) => $"price_{ticker}";
    }
}
