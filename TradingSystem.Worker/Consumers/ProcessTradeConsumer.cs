using System;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingSystem.Application.Commands;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Worker.Consumers
{
    public class ProcessTradeConsumer : IConsumer<ProcessTradeCommand>
    {
        private readonly ILogger<ProcessTradeConsumer> _logger;
        private readonly TradingDbContext _dbContext;

        public ProcessTradeConsumer(ILogger<ProcessTradeConsumer> logger, TradingDbContext dbContext)
        {
            _logger = logger;
            _dbContext = dbContext;
        }

        public async Task Consume(ConsumeContext<ProcessTradeCommand> context)
        {
            var command = context.Message;
            bool saved = false;

            while (!saved)
            {
                try
                {
                    // 1. Fetch Order (IDEMPOTENCY CHECK)
                    var order = await _dbContext.TradeOrders.FindAsync(command.OrderId);
                    if (order == null || order.IsProcessed)
                    {
                        _logger.LogInformation($"Order {command.OrderId} already processed or invalid. Skipping.");
                        return;
                    }

                    // 2. Fetch Global Stock Price
                    var stock = await _dbContext.StockPrices.FindAsync(order.StockTicker);
                    if (stock == null) return;

                    // 3. Math: Weighted Average Formula
                    // Price moves towards the order price by a fraction of the volume.
                    // $NewPrice = CurrentPrice + (OrderVolume / TotalVolume) * (OrderPrice - CurrentPrice)$
                    decimal fraction = order.Volume / stock.TotalStockVolume;
                    decimal priceDifference = order.BidAmount - stock.CurrentPrice;

                    stock.CurrentPrice += (fraction * priceDifference);

                    // 4. Update Aggregates
                    if (order.IsBuy) stock.BuyVolume += order.Volume;
                    else stock.SellVolume += order.Volume;

                    // 5. Mark as Processed
                    order.IsProcessed = true;

                    // 6. Attempt Save (Optimistic Concurrency trigger)
                    await _dbContext.SaveChangesAsync();
                    saved = true;

                    _logger.LogInformation($"[Market Update] {stock.Ticker} is now ${stock.CurrentPrice:F4}. Triggered by Server: {order.ServerId}");
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    // CONCURRENCY HANDLING: Another server updated the price at the exact same millisecond.
                    // We discard our math, reload the *newest* price from the database, and let the while loop try again.
                    foreach (var entry in ex.Entries)
                    {
                        await entry.ReloadAsync();
                    }
                    _logger.LogWarning($"[Concurrency Collision] Recalculating price for order {command.OrderId}...");
                }
            }
        }
    }
}