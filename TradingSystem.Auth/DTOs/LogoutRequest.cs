namespace TradingSystem.Auth.DTOs
{
    public sealed class LogoutRequest
    {
        public string? RefreshToken { get; set; }
    }
}
