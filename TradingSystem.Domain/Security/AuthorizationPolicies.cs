namespace TradingSystem.Domain.Security
{
    public static class AuthorizationPolicies
    {
        public const string AccountsManage = "policy.accounts.manage";
        public const string TasksRead = "policy.tasks.read";
        public const string TasksManage = "policy.tasks.manage";
        public const string PricesRead = "policy.prices.read";
        public const string TradesPlace = "policy.trades.place";
    }
}
