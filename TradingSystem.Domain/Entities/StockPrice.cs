using System;
using System.Collections.Generic;
using System.Text;

namespace TradingSystem.Domain.Entities
{
    public class StockPrice
    {
        public string Ticker { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal TotalStockVolume { get; set; }
        public decimal BuyVolume { get; set; }
        public decimal SellVolume { get; set; }
        public DateTime RowVersion { get; set; }
    }
}