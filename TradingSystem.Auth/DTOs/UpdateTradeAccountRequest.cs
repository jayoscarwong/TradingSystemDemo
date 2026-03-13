using System.ComponentModel.DataAnnotations;

namespace TradingSystem.Auth.DTOs
{
    public sealed class UpdateTradeAccountRequest
    {
        [MaxLength(200)]
        public string? Name { get; set; }

        [MaxLength(100)]
        public string? Username { get; set; }

        [EmailAddress]
        [MaxLength(200)]
        public string? Email { get; set; }

        [MinLength(8)]
        public string? Password { get; set; }

        public string[]? GroupNames { get; set; }
    }
}
