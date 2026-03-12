using System;
using System.ComponentModel.DataAnnotations;

namespace TradingSystem.Domain.Entities
{
    public class TradeOrder
    {
        public Guid Id { get; set; }
        public string StockTicker { get; set; }
        public decimal BidAmount { get; set; }
        public string ServerId { get; set; }
        
       
        public byte RowVersion { get; set; }
    }
}
