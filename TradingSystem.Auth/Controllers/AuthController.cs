using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Auth.DTOs;
using TradingSystem.Auth.Services;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Auth.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly TradingDbContext _dbContext;
        private readonly PasswordHashService _passwordHashService;
        private readonly JwtTokenService _jwtTokenService;
        private readonly TradeSessionCacheService _sessionCacheService;

        public AuthController(
            TradingDbContext dbContext,
            PasswordHashService passwordHashService,
            JwtTokenService jwtTokenService,
            TradeSessionCacheService sessionCacheService)
        {
            _dbContext = dbContext;
            _passwordHashService = passwordHashService;
            _jwtTokenService = jwtTokenService;
            _sessionCacheService = sessionCacheService;
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            var normalizedLogin = request.Username.Trim();

            var account = await _dbContext.TradeAccounts
                .Include(existing => existing.AccountGroups)
                    .ThenInclude(link => link.TradeUserGroup)
                        .ThenInclude(group => group!.GroupPermissions)
                            .ThenInclude(link => link.TradePermission)
                .SingleOrDefaultAsync(existing =>
                    existing.Username == normalizedLogin ||
                    existing.Email == normalizedLogin,
                    cancellationToken);

            if (account == null || account.IsDisabled || !_passwordHashService.Verify(request.Password, account.PasswordHash, account.PasswordSalt))
            {
                return Unauthorized(new { Message = "Invalid username/email or password." });
            }

            var groups = account.AccountGroups
                .Select(link => link.TradeUserGroup!.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var permissions = account.AccountGroups
                .SelectMany(link => link.TradeUserGroup!.GroupPermissions)
                .Select(link => link.TradePermission!.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            account.LastLoginAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            var session = await _sessionCacheService.StartSessionAsync(account.Username, account.Id, cancellationToken);
            var token = _jwtTokenService.CreateToken(account, groups, permissions, session.SessionId);

            return Ok(new
            {
                token.AccessToken,
                token.ExpiresAtUtc,
                SessionId = session.SessionId,
                Account = new
                {
                    account.Id,
                    account.Name,
                    account.Username,
                    account.Email,
                    account.LastLoginAt
                },
                Groups = groups,
                Permissions = permissions
            });
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout(CancellationToken cancellationToken)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
            {
                return BadRequest(new { Message = "Username claim is missing." });
            }

            await _sessionCacheService.RevokeAsync(username, cancellationToken);
            return Ok(new { Message = $"User '{username}' has been logged out." });
        }

        [HttpGet("me")]
        [Authorize]
        public IActionResult Me()
        {
            return Ok(new
            {
                Id = User.FindFirstValue(ClaimTypes.NameIdentifier),
                Username = User.Identity?.Name,
                Email = User.FindFirstValue(ClaimTypes.Email),
                DisplayName = User.FindFirstValue("display_name"),
                Groups = User.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray(),
                Permissions = User.FindAll("permission").Select(claim => claim.Value).ToArray()
            });
        }
    }
}
