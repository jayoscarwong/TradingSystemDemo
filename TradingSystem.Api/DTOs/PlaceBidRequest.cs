using System.ComponentModel.DataAnnotations;

namespace TradingSystem.Api.DTOs
{
    public class PlaceBidRequest
    {
        public Guid OrderId { get; set; }

        [Required]
        [MaxLength(10)]
        public required string StockTicker { get; set; }

        [Range(typeof(decimal), "0.0001", "79228162514264337593543950335")]
        public decimal BidAmount { get; set; }

        [Range(typeof(decimal), "0.0001", "79228162514264337593543950335")]
        public decimal Volume { get; set; }

        public bool IsBuy { get; set; }

        [Range(1, int.MaxValue)]
        public int ServerId { get; set; }
    }
}
