using System.ComponentModel.DataAnnotations;

namespace TradingSystem.Auth.DTOs
{
    public sealed class RegisterTradeAccountRequest
    {
        [Required]
        [MaxLength(200)]
        public required string Name { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Username { get; set; }

        [Required]
        [EmailAddress]
        [MaxLength(200)]
        public required string Email { get; set; }

        [Required]
        [MinLength(8)]
        public required string Password { get; set; }

        [Required]
        [MaxLength(50)]
        public required string RequestedGroup { get; set; }
    }
}
