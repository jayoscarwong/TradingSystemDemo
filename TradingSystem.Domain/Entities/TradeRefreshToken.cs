namespace TradingSystem.Domain.Entities
{
    public class TradeRefreshToken
    {
        public long Id { get; set; }
        public long TradeAccountId { get; set; }
        public required string TokenHash { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? RevokedAt { get; set; }
        public string? ReplacedByTokenHash { get; set; }

        public TradeAccount? TradeAccount { get; set; }
    }
}
