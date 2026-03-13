namespace TradingSystem.Domain.Entities
{
    public class TradeAccountGroup
    {
        public long TradeAccountId { get; set; }
        public int TradeUserGroupId { get; set; }

        public TradeAccount? TradeAccount { get; set; }
        public TradeUserGroup? TradeUserGroup { get; set; }
    }
}
