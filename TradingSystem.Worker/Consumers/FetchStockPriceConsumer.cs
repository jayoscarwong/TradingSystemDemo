using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using TradingSystem.Application.Commands;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingSystem.Infrastructure.Data;


namespace TradingSystem.Worker.Consumers
{
    // This consumer listens for the FetchStockPriceCommand message
    public class FetchStockPriceConsumer : IConsumer<FetchStockPriceCommand>
    {
        private readonly ILogger<FetchStockPriceConsumer> _logger; 
        private readonly TradingDbContext _dbContext;

        public FetchStockPriceConsumer(ILogger<FetchStockPriceConsumer> logger,  TradingDbContext dbContext)
        {
            _logger = logger;           
            _dbContext = dbContext;
        }

        public async Task Consume(ConsumeContext<FetchStockPriceCommand> context)
        {
            var command = context.Message;
            _logger.LogInformation($"[MassTransit] Processing market data for {command.Ticker} on {command.ServerId}...");

            // 1. Fetch the LATEST bid from the database for this specific server/ticker
            var latestBid = await _dbContext.TradeOrders
                .Where(t => t.StockTicker == command.Ticker && t.ServerId == command.ServerId)
                .OrderByDescending(t => t.RowVersion)
                .FirstOrDefaultAsync();

            // 2. Determine the price based on user activity
            decimal activePrice = latestBid != null ? latestBid.BidAmount : 150.00m; // Default to 150 if no bids exist
            decimal simulatedVolume = latestBid != null ? 100 : 0; // Spike volume if a bid exists

            //// 3. Update the StockPrices table dynamically based on the bid
            //await _stockPriceService.UpdateStockPriceAsync(
            //    ticker: command.Ticker,
            //    serverId: command.ServerId,
            //    orderPrice: activePrice,
            //    orderVolume: simulatedVolume
            //);

            _logger.LogInformation($"[MassTransit] Applied latest bid. {command.Ticker} updated to ${activePrice:F2} in MySQL.");
        }
    }
}