namespace TradingSystem.Application.Commands
{
    public record FetchStockPriceCommand
    {
        public required string Ticker { get; init; }
        public required int ServerId { get; init; }
    }
}
