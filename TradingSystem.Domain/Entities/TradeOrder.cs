using System;
using System.ComponentModel.DataAnnotations;

namespace TradingSystem.Domain.Entities
{
    public class TradeOrder
    {
        public Guid Id { get; set; }
        public required string StockTicker { get; set; }
        public decimal BidAmount { get; set; }
        public decimal Volume { get; set; }
        public bool IsBuy { get; set; }
        public required int ServerId { get; set; } // Changed to int
        public bool IsProcessed { get; set; }
        public DateTime RowVersion { get; set; }
    }
}
