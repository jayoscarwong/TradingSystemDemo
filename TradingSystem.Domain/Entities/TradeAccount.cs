namespace TradingSystem.Domain.Entities
{
    public class TradeAccount
    {
        public long Id { get; set; }
        public required string Name { get; set; }
        public required string Username { get; set; }
        public required string Email { get; set; }
        public required byte[] PasswordHash { get; set; }
        public required byte[] PasswordSalt { get; set; }
        public bool IsDisabled { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public DateTime RowVersion { get; set; }

        public ICollection<TradeAccountGroup> AccountGroups { get; set; } = new List<TradeAccountGroup>();
        public ICollection<TradeRefreshToken> RefreshTokens { get; set; } = new List<TradeRefreshToken>();
        public ICollection<TradeOrder> TradeOrders { get; set; } = new List<TradeOrder>();
    }
}
