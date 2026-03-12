namespace TradingSystem.Application.Commands
{
    public record FetchStockPriceCommand
    {
        public string Ticker { get; init; }
        public string ServerId { get; init; }
    }
}
