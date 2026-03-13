using System;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingSystem.Application.Commands;
using TradingSystem.Infrastructure.Data;
using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using TradingSystem.Domain.Entities;

namespace TradingSystem.Worker.Consumers
{
    public class ProcessTradeConsumer : IConsumer<ProcessTradeCommand>
    {
        private readonly ILogger<ProcessTradeConsumer> _logger;
        private readonly TradingDbContext _dbContext;
        private readonly IDistributedCache _redisCache;

        public ProcessTradeConsumer(ILogger<ProcessTradeConsumer> logger, TradingDbContext dbContext, IDistributedCache redisCache)
        {
            _logger = logger;
            _dbContext = dbContext;
            _redisCache = redisCache;
        }
        public async Task Consume(ConsumeContext<ProcessTradeCommand> context)
        {
            var command = context.Message;
            bool saved = false;

            while (!saved)
            {
                try
                {
                    var order = await _dbContext.TradeOrders.FindAsync(command.OrderId);
                    if (order == null || order.IsProcessed) return; // Idempotency check on the consumer side

                    var stock = await _dbContext.StockPrices.FindAsync(order.StockTicker);
                    if (stock == null) return;

                    // --- REAL-TIME PRICING LOGIC ---
                    decimal volatilityFactor = 0.05m; // 5% base volatility

                    if (order.IsBuy)
                    {
                        stock.BuyVolume += order.Volume;
                        stock.AvailableVolume -= order.Volume;

                        // OverBuy Scenario: Buying more than what is available
                        if (stock.AvailableVolume < 0)
                        {
                            // Price spikes aggressively because supply is depleted
                            decimal overBoughtRatio = Math.Abs(stock.AvailableVolume) / stock.TotalStockVolume;
                            stock.CurrentPrice *= (1 + (volatilityFactor * 2) + overBoughtRatio);

                            _logger.LogWarning($"[OVERBUY ALERT] {stock.Ticker} liquidity depleted! Price spiked to {stock.CurrentPrice:F2}");
                        }
                        else
                        {
                            // Normal Buy: Price goes up slightly based on volume weight
                            decimal impact = (order.Volume / stock.TotalStockVolume) * volatilityFactor;
                            stock.CurrentPrice *= (1 + impact);
                        }
                    }
                    else // Is Sell
                    {
                        stock.SellVolume += order.Volume;
                        stock.AvailableVolume += order.Volume;

                        // OverSell Scenario: Dumping massive amounts of stock
                        if (order.Volume > (stock.TotalStockVolume * 0.1m)) // Selling more than 10% of total volume at once
                        {
                            decimal dumpRatio = order.Volume / stock.TotalStockVolume;
                            stock.CurrentPrice *= (1 - (volatilityFactor * 2) - dumpRatio);
                            _logger.LogWarning($"[OVERSELL ALERT] {stock.Ticker} massive dump! Price crashed to {stock.CurrentPrice:F2}");
                        }
                        else
                        {
                            // Normal Sell: Price goes down slightly
                            decimal impact = (order.Volume / stock.TotalStockVolume) * volatilityFactor;
                            stock.CurrentPrice *= (1 - impact);
                        }
                    }

                    // Prevent price from hitting zero or negative
                    if (stock.CurrentPrice < 0.01m) stock.CurrentPrice = 0.01m;

                    stock.LastUpdatedAt = DateTime.UtcNow;
                    order.IsProcessed = true;

                    // Optimistic Concurrency Save (RowVersion handles collisions here)
                    await _dbContext.SaveChangesAsync();
                    saved = true;

                    // 4. Update Cache-Aside for High-Speed API Reads
                    var cacheKey = $"price_{stock.Ticker}";
                    await _redisCache.SetStringAsync(cacheKey, JsonSerializer.Serialize(stock));

                    _logger.LogInformation($"[Market] {stock.Ticker} processed. New Price: ${stock.CurrentPrice:F4}. Available: {stock.AvailableVolume}");
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    // Collision occurred! Another order updated the price first. 
                    // Reload the freshest data from MySQL and run the math again.
                    foreach (var entry in ex.Entries) await entry.ReloadAsync();
                }
            }
        }

        //public async Task Consume(ConsumeContext<ProcessTradeCommand> context)
        //{
        //    var command = context.Message;
        //    bool saved = false;

        //    while (!saved)
        //    {
        //        try
        //        {
        //            // 1. Fetch Order (IDEMPOTENCY CHECK)
        //            var order = await _dbContext.TradeOrders.FindAsync(command.OrderId);
        //            if (order == null || order.IsProcessed)
        //            {
        //                _logger.LogInformation($"Order {command.OrderId} already processed or invalid. Skipping.");
        //                return;
        //            }

        //            // 2. Fetch Global Stock Price
        //            var stock = await _dbContext.StockPrices.FindAsync(order.StockTicker);
        //            if (stock == null) return;

        //            // 3. Math: Weighted Average Formula
        //            // Price moves towards the order price by a fraction of the volume.
        //            // $NewPrice = CurrentPrice + (OrderVolume / TotalVolume) * (OrderPrice - CurrentPrice)$
        //            decimal fraction = order.Volume / stock.TotalStockVolume;
        //            decimal priceDifference = order.BidAmount - stock.CurrentPrice;

        //            stock.CurrentPrice += (fraction * priceDifference);

        //            // 4. Update Aggregates
        //            if (order.IsBuy) stock.BuyVolume += order.Volume;
        //            else stock.SellVolume += order.Volume;

        //            // 5. Mark as Processed
        //            order.IsProcessed = true;

        //            // 6. Attempt Save (Optimistic Concurrency trigger)
        //            await _dbContext.SaveChangesAsync();
        //            saved = true;

        //            _logger.LogInformation($"[Market Update] {stock.Ticker} is now ${stock.CurrentPrice:F4}. Triggered by Server: {order.ServerId}");
        //        }
        //        catch (DbUpdateConcurrencyException ex)
        //        {
        //            // CONCURRENCY HANDLING: Another server updated the price at the exact same millisecond.
        //            // We discard our math, reload the *newest* price from the database, and let the while loop try again.
        //            foreach (var entry in ex.Entries)
        //            {
        //                await entry.ReloadAsync();
        //            }
        //            _logger.LogWarning($"[Concurrency Collision] Recalculating price for order {command.OrderId}...");
        //        }
        //    }
        //}
    }
}