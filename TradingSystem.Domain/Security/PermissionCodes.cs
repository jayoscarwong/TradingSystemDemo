namespace TradingSystem.Domain.Security
{
    public static class PermissionCodes
    {
        public const string AccountsManage = "accounts.manage";
        public const string TasksRead = "tasks.read";
        public const string TasksManage = "tasks.manage";
        public const string PricesRead = "prices.read";
        public const string TradesPlace = "trades.place";

        public static readonly string[] All =
        {
            AccountsManage,
            TasksRead,
            TasksManage,
            PricesRead,
            TradesPlace
        };
    }
}
