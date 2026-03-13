using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Auth.DTOs;
using TradingSystem.Auth.Services;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Security;
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
        private readonly RefreshTokenService _refreshTokenService;
        private readonly TradeSessionCacheService _sessionCacheService;

        public AuthController(
            TradingDbContext dbContext,
            PasswordHashService passwordHashService,
            JwtTokenService jwtTokenService,
            RefreshTokenService refreshTokenService,
            TradeSessionCacheService sessionCacheService)
        {
            _dbContext = dbContext;
            _passwordHashService = passwordHashService;
            _jwtTokenService = jwtTokenService;
            _refreshTokenService = refreshTokenService;
            _sessionCacheService = sessionCacheService;
        }

        /// <summary>
        /// Authenticates a trade account and returns access and refresh tokens.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     POST /api/Auth/login
        ///     {
        ///       "username": "admin",
        ///       "password": "Admin123!ChangeMe"
        ///     }
        ///
        /// If the credentials are correct but the account is disabled, the response returns the account status and permissions without issuing tokens.
        /// </remarks>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            var normalizedLogin = request.Username.Trim();

            var account = await QueryAccounts()
                .SingleOrDefaultAsync(existing =>
                    existing.Username == normalizedLogin ||
                    existing.Email == normalizedLogin,
                    cancellationToken);

            if (account == null || !_passwordHashService.Verify(request.Password, account.PasswordHash, account.PasswordSalt))
            {
                return Unauthorized(new { Message = "Invalid username/email or password." });
            }

            var (groups, permissions) = ExtractAccess(account);
            if (account.IsDisabled)
            {
                return StatusCode(StatusCodes.Status423Locked, new
                {
                    Message = "Account is disabled pending admin approval.",
                    Account = ToResponse(account, groups, permissions)
                });
            }

            account.LastLoginAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Ok(await IssueTokensAsync(account, groups, permissions, cancellationToken));
        }

        /// <summary>
        /// Exchanges a valid refresh token for a new access token and a rotated refresh token.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     POST /api/Auth/refresh
        ///     {
        ///       "refreshToken": "paste-refresh-token-here"
        ///     }
        /// </remarks>
        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request, CancellationToken cancellationToken)
        {
            var incomingTokenHash = _refreshTokenService.ComputeHash(request.RefreshToken.Trim());
            var refreshToken = await _dbContext.TradeRefreshTokens
                .Include(token => token.TradeAccount)
                    .ThenInclude(account => account!.AccountGroups)
                        .ThenInclude(link => link.TradeUserGroup)
                            .ThenInclude(group => group!.GroupPermissions)
                                .ThenInclude(link => link.TradePermission)
                .SingleOrDefaultAsync(token => token.TokenHash == incomingTokenHash, cancellationToken);

            if (refreshToken == null || refreshToken.RevokedAt.HasValue || refreshToken.ExpiresAt <= DateTime.UtcNow)
            {
                return Unauthorized(new { Message = "Refresh token is invalid or expired." });
            }

            var account = refreshToken.TradeAccount!;
            var (groups, permissions) = ExtractAccess(account);
            if (account.IsDisabled)
            {
                await RevokeRefreshTokensAsync(account.Id, cancellationToken);
                await _sessionCacheService.RevokeAsync(account.Username, cancellationToken);

                return StatusCode(StatusCodes.Status423Locked, new
                {
                    Message = "Account is disabled pending admin approval.",
                    Account = ToResponse(account, groups, permissions)
                });
            }

            var replacement = _refreshTokenService.Create();
            refreshToken.RevokedAt = DateTime.UtcNow;
            refreshToken.ReplacedByTokenHash = replacement.TokenHash;

            _dbContext.TradeRefreshTokens.Add(new TradeRefreshToken
            {
                TradeAccountId = account.Id,
                TokenHash = replacement.TokenHash,
                CreatedAt = replacement.CreatedAtUtc,
                ExpiresAt = replacement.ExpiresAtUtc
            });

            await _dbContext.SaveChangesAsync(cancellationToken);

            var authResult = await IssueTokensAsync(account, groups, permissions, replacement, cancellationToken);
            return Ok(authResult);
        }

        /// <summary>
        /// Logs the current account out and revokes its active refresh tokens.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     POST /api/Auth/logout
        ///     {
        ///       "refreshToken": "optional-refresh-token-to-revoke"
        ///     }
        /// </remarks>
        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest? request, CancellationToken cancellationToken)
        {
            var username = User.Identity?.Name;
            var accountIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(username) || !long.TryParse(accountIdClaim, out var accountId))
            {
                return BadRequest(new { Message = "Authenticated account information is missing from the token." });
            }

            await _sessionCacheService.RevokeAsync(username, cancellationToken);

            if (!string.IsNullOrWhiteSpace(request?.RefreshToken))
            {
                var tokenHash = _refreshTokenService.ComputeHash(request.RefreshToken.Trim());
                var token = await _dbContext.TradeRefreshTokens
                    .SingleOrDefaultAsync(existing =>
                        existing.TradeAccountId == accountId &&
                        existing.TokenHash == tokenHash,
                        cancellationToken);

                if (token != null && !token.RevokedAt.HasValue)
                {
                    token.RevokedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }

            await RevokeRefreshTokensAsync(accountId, cancellationToken);

            return Ok(new { Message = $"User '{username}' has been logged out." });
        }

        /// <summary>
        /// Returns the current authenticated account status, groups, and permissions.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     GET /api/Auth/me
        /// </remarks>
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me(CancellationToken cancellationToken)
        {
            var accountIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!long.TryParse(accountIdClaim, out var accountId))
            {
                return BadRequest(new { Message = "Authenticated account information is missing from the token." });
            }

            var account = await QueryAccounts()
                .AsNoTracking()
                .SingleOrDefaultAsync(existing => existing.Id == accountId, cancellationToken);

            if (account == null)
            {
                return NotFound(new { Message = $"Trade account {accountId} was not found." });
            }

            var (groups, permissions) = ExtractAccess(account);
            return Ok(new
            {
                Account = ToResponse(account, groups, permissions),
                TokenGroups = User.FindAll(CustomClaimTypes.Group).Select(claim => claim.Value).ToArray(),
                TokenPermissions = User.FindAll(CustomClaimTypes.Permission).Select(claim => claim.Value).ToArray()
            });
        }

        private IQueryable<TradeAccount> QueryAccounts()
        {
            return _dbContext.TradeAccounts
                .Include(existing => existing.AccountGroups)
                    .ThenInclude(link => link.TradeUserGroup)
                        .ThenInclude(group => group!.GroupPermissions)
                            .ThenInclude(link => link.TradePermission);
        }

        private async Task<object> IssueTokensAsync(
            TradeAccount account,
            string[] groups,
            string[] permissions,
            CancellationToken cancellationToken)
        {
            var refreshToken = _refreshTokenService.Create();

            _dbContext.TradeRefreshTokens.Add(new TradeRefreshToken
            {
                TradeAccountId = account.Id,
                TokenHash = refreshToken.TokenHash,
                CreatedAt = refreshToken.CreatedAtUtc,
                ExpiresAt = refreshToken.ExpiresAtUtc
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            return await IssueTokensAsync(account, groups, permissions, refreshToken, cancellationToken);
        }

        private async Task<object> IssueTokensAsync(
            TradeAccount account,
            string[] groups,
            string[] permissions,
            RefreshTokenPayload refreshToken,
            CancellationToken cancellationToken)
        {
            var session = await _sessionCacheService.StartSessionAsync(account.Username, account.Id, cancellationToken);
            var token = _jwtTokenService.CreateToken(account, groups, permissions, session.SessionId);

            return new
            {
                token.AccessToken,
                token.ExpiresAtUtc,
                RefreshToken = refreshToken.Token,
                RefreshTokenExpiresAtUtc = refreshToken.ExpiresAtUtc,
                SessionId = session.SessionId,
                Account = ToResponse(account, groups, permissions),
                Groups = groups,
                Permissions = permissions
            };
        }

        private async Task RevokeRefreshTokensAsync(long tradeAccountId, CancellationToken cancellationToken)
        {
            var activeTokens = await _dbContext.TradeRefreshTokens
                .Where(token => token.TradeAccountId == tradeAccountId && !token.RevokedAt.HasValue)
                .ToListAsync(cancellationToken);

            if (activeTokens.Count == 0)
            {
                return;
            }

            var revokedAtUtc = DateTime.UtcNow;
            foreach (var token in activeTokens)
            {
                token.RevokedAt = revokedAtUtc;
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        private static (string[] Groups, string[] Permissions) ExtractAccess(TradeAccount account)
        {
            var groups = account.AccountGroups
                .Select(link => link.TradeUserGroup!.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name)
                .ToArray();
            var permissions = account.AccountGroups
                .SelectMany(link => link.TradeUserGroup!.GroupPermissions)
                .Select(link => link.TradePermission!.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code)
                .ToArray();

            return (groups, permissions);
        }

        private static object ToResponse(TradeAccount account, string[] groups, string[] permissions)
        {
            return new
            {
                account.Id,
                account.Name,
                account.Username,
                account.Email,
                account.IsDisabled,
                account.CreatedAt,
                account.LastLoginAt,
                Groups = groups,
                Permissions = permissions
            };
        }
    }
}
