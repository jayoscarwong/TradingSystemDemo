using System.ComponentModel.DataAnnotations;

namespace TradingSystem.Auth.DTOs
{
    public sealed class LoginRequest
    {
        [Required]
        [MaxLength(200)]
        public required string Username { get; set; }

        [Required]
        [MinLength(8)]
        public required string Password { get; set; }
    }
}
