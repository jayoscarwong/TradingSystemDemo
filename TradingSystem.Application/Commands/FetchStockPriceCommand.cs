namespace TradingSystem.Application.Commands
{
    public record FetchStockPriceCommand
    {
        public required string Ticker { get; init; }
        public required string ServerId { get; init; }
    }
}
