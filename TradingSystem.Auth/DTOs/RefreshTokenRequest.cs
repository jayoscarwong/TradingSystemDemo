using System.ComponentModel.DataAnnotations;

namespace TradingSystem.Auth.DTOs
{
    public sealed class RefreshTokenRequest
    {
        [Required]
        public required string RefreshToken { get; set; }
    }
}
