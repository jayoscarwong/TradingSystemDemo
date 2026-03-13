namespace TradingSystem.Domain.Entities
{
    public class TradeUserGroup
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public required string Description { get; set; }
        public bool IsSystemGroup { get; set; }

        public ICollection<TradeAccountGroup> AccountGroups { get; set; } = new List<TradeAccountGroup>();
        public ICollection<TradeGroupPermission> GroupPermissions { get; set; } = new List<TradeGroupPermission>();
    }
}
