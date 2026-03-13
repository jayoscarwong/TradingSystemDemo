using System;

namespace TradingSystem.Domain.Entities
{
    public class TradeOrder
    {
        public Guid Id { get; set; }
        public required string StockTicker { get; set; }
        public decimal BidAmount { get; set; }
        public decimal Volume { get; set; }
        public bool IsBuy { get; set; }
        public required int ServerId { get; set; }
        public decimal ExecutedVolume { get; set; }
        public decimal QueuedVolume { get; set; }
        public bool IsProcessed { get; set; }
        public required string Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime RowVersion { get; set; }
    }
}
