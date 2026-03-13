namespace TradingSystem.Auth.Options
{
    public sealed class AuthenticationSettings
    {
        public string Issuer { get; set; } = "TradingSystem.Auth";
        public string Audience { get; set; } = "TradingSystem.Clients";
        public string SigningKey { get; set; } = "ChangeThisToALongerDevelopmentSigningKey1234567890";
        public int SessionMinutes { get; set; } = 30;
        public int RefreshTokenDays { get; set; } = 7;
        public string BootstrapAdminPassword { get; set; } = "Admin123!ChangeMe";
    }
}
