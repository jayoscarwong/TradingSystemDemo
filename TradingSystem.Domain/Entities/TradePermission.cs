namespace TradingSystem.Domain.Entities
{
    public class TradePermission
    {
        public int Id { get; set; }
        public required string Code { get; set; }
        public required string Description { get; set; }

        public ICollection<TradeGroupPermission> GroupPermissions { get; set; } = new List<TradeGroupPermission>();
    }
}
