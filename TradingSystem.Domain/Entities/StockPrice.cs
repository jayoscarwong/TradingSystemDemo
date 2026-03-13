using System;

namespace TradingSystem.Domain.Entities
{
    public class StockPrice
    {
        public required string Ticker { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal TotalStockVolume { get; set; }
        public decimal BuyVolume { get; set; }
        public decimal SellVolume { get; set; }
        public DateTime LastUpdatedAt { get; set; } // <-- Added back
        public DateTime RowVersion { get; set; }
    }
}