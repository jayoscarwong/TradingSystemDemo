namespace TradingSystem.Api.DTOs
{
    public class PlaceBidRequest
    {
        public required string StockTicker { get; set; }
        public decimal BidAmount { get; set; }
        public decimal Volume { get; set; }
        public bool IsBuy { get; set; }
        public int ServerId { get; set; }
    }
}
