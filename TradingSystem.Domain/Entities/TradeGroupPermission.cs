namespace TradingSystem.Domain.Entities
{
    public class TradeGroupPermission
    {
        public int TradeUserGroupId { get; set; }
        public int TradePermissionId { get; set; }

        public TradeUserGroup? TradeUserGroup { get; set; }
        public TradePermission? TradePermission { get; set; }
    }
}
