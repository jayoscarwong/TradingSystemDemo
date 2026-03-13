namespace TradingSystem.Application.Commands
{
    public record FetchStockPriceCommand
    {
        public required string Ticker { get; init; }

        // FIX: Changed to int
        public required int ServerId { get; init; }
    }
}