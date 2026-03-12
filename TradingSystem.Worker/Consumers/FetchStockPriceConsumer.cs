using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.Logging;
using TradingSystem.Application.Commands;

namespace TradingSystem.Worker.Consumers
{
    // This consumer listens for the FetchStockPriceCommand message
    public class FetchStockPriceConsumer : IConsumer<FetchStockPriceCommand>
    {
        private readonly ILogger<FetchStockPriceConsumer> _logger;

        public FetchStockPriceConsumer(ILogger<FetchStockPriceConsumer> logger)
        {
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<FetchStockPriceCommand> context)
        {
            var command = context.Message;

            _logger.LogInformation($"[MassTransit] Received fetch command. Ticker: {command.Ticker} | Server: {command.ServerId}");

            // TODO: Here is where you would call an external API (like Yahoo Finance, Polygon.io) 
            // to get the real stock price using the command.Ticker.

            // Simulating API network delay
            await Task.Delay(500);

            _logger.LogInformation($"[MassTransit] Successfully processed and pulled data for {command.Ticker}");
        }
    }
}