using System.Security.Claims;
using System.Text.Json;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using TradingSystem.Api.DTOs;
using TradingSystem.Application.Commands;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Security;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TradesController : ControllerBase
    {
        private static readonly DistributedCacheEntryOptions PriceCacheOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        };

        private readonly TradingDbContext _dbContext;
        private readonly IPublishEndpoint _publishEndpoint;
        private readonly IDistributedCache _redisCache;

        public TradesController(TradingDbContext dbContext, IPublishEndpoint publishEndpoint, IDistributedCache redisCache)
        {
            _dbContext = dbContext;
            _publishEndpoint = publishEndpoint;
            _redisCache = redisCache;
        }

        /// <summary>
        /// Places an authenticated trade order using a client-supplied idempotency GUID.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     POST /api/Trades
        ///     {
        ///       "orderId": "5f9042c1-f4c9-4fd9-8d3a-0ec1ecb9e3c5",
        ///       "stockTicker": "AAPL",
        ///       "bidAmount": 191.2500,
        ///       "volume": 25,
        ///       "isBuy": true,
        ///       "serverId": 1
        ///     }
        ///
        /// Reusing the same <c>orderId</c> with the exact same payload is treated as a safe retry.
        /// </remarks>
        [HttpPost]
        [Authorize(Policy = AuthorizationPolicies.TradesPlace)]
        public async Task<IActionResult> PlaceBid([FromBody] PlaceBidRequest request)
        {
            var tradeAccountIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!long.TryParse(tradeAccountIdClaim, out var tradeAccountId))
            {
                return Unauthorized(new { Message = "Authenticated trade account information is missing from the token." });
            }

            var tradeAccountExists = await _dbContext.TradeAccounts
                .AsNoTracking()
                .AnyAsync(account => account.Id == tradeAccountId && !account.IsDisabled);

            if (!tradeAccountExists)
            {
                return Unauthorized(new { Message = "The authenticated trade account is disabled or no longer exists." });
            }

            if (request.OrderId == Guid.Empty)
            {
                return BadRequest(new { Message = "OrderId must be a non-empty GUID." });
            }

            var normalizedTicker = request.StockTicker.Trim().ToUpperInvariant();

            var tradingServerExists = await _dbContext.TradingServers
                .AsNoTracking()
                .AnyAsync(server => server.Id == request.ServerId && server.IsEnabled);

            if (!tradingServerExists)
            {
                return BadRequest(new { Message = $"Trading server {request.ServerId} is not enabled." });
            }

            var stockExists = await _dbContext.StockPrices
                .AsNoTracking()
                .AnyAsync(stock => stock.Ticker == normalizedTicker);

            if (!stockExists)
            {
                return NotFound(new { Message = $"Ticker '{normalizedTicker}' is not configured." });
            }

            var existingOrder = await _dbContext.TradeOrders
                .AsNoTracking()
                .SingleOrDefaultAsync(order => order.Id == request.OrderId);

            if (existingOrder is not null)
            {
                return BuildDuplicateRequestResponse(existingOrder, request, normalizedTicker, tradeAccountId);
            }

            var utcNow = DateTime.UtcNow;
            var order = new TradeOrder
            {
                Id = request.OrderId,
                TradeAccountId = tradeAccountId,
                StockTicker = normalizedTicker,
                BidAmount = request.BidAmount,
                Volume = request.Volume,
                IsBuy = request.IsBuy,
                ServerId = request.ServerId,
                ExecutedVolume = 0m,
                QueuedVolume = request.Volume,
                Status = "Pending",
                CreatedAt = utcNow,
                ProcessedAt = null,
                IsProcessed = false
            };

            _dbContext.TradeOrders.Add(order);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                _dbContext.Entry(order).State = EntityState.Detached;

                existingOrder = await _dbContext.TradeOrders
                    .AsNoTracking()
                    .SingleOrDefaultAsync(existing => existing.Id == request.OrderId);

                if (existingOrder is null)
                {
                    throw;
                }

                return BuildDuplicateRequestResponse(existingOrder, request, normalizedTicker, tradeAccountId);
            }

            await _publishEndpoint.Publish(new ProcessTradeCommand { OrderId = order.Id });

            return Accepted(new
            {
                Message = "Order received and queued for real-time processing.",
                OrderId = order.Id,
                order.Status,
                order.IsProcessed,
                order.ExecutedVolume,
                order.QueuedVolume
            });
        }

        /// <summary>
        /// Reads the latest cached price snapshot for a ticker, falling back to MySQL on a cache miss.
        /// </summary>
        /// <remarks>
        /// Sample request:
        ///
        ///     GET /api/Trades/price/AAPL
        /// </remarks>
        [HttpGet("price/{ticker}")]
        [Authorize(Policy = AuthorizationPolicies.PricesRead)]
        public async Task<IActionResult> GetRealTimePrice(string ticker)
        {
            var normalizedTicker = ticker.Trim().ToUpperInvariant();
            var cacheKey = GetPriceCacheKey(normalizedTicker);
            var cachedPrice = await _redisCache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedPrice))
            {
                return Ok(JsonSerializer.Deserialize<StockPrice>(cachedPrice));
            }

            var stockPrice = await _dbContext.StockPrices
                .AsNoTracking()
                .FirstOrDefaultAsync(stock => stock.Ticker == normalizedTicker);

            if (stockPrice == null)
            {
                return NotFound(new { Message = "Stock not found." });
            }

            await _redisCache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(stockPrice),
                PriceCacheOptions);

            return Ok(stockPrice);
        }

        private IActionResult BuildDuplicateRequestResponse(TradeOrder existingOrder, PlaceBidRequest request, string normalizedTicker, long tradeAccountId)
        {
            if (!MatchesExistingOrder(existingOrder, request, normalizedTicker, tradeAccountId))
            {
                return Conflict(new
                {
                    Message = "OrderId already exists with a different payload. Reuse the original payload or send a new GUID.",
                    OrderId = existingOrder.Id,
                    existingOrder.Status
                });
            }

            return Accepted(new
            {
                Message = "Duplicate request ignored. Returning the existing order state.",
                OrderId = existingOrder.Id,
                existingOrder.Status,
                existingOrder.IsProcessed,
                existingOrder.ExecutedVolume,
                existingOrder.QueuedVolume,
                existingOrder.ProcessedAt
            });
        }

        private static bool MatchesExistingOrder(TradeOrder existingOrder, PlaceBidRequest request, string normalizedTicker, long tradeAccountId)
        {
            return existingOrder.TradeAccountId == tradeAccountId
                && existingOrder.StockTicker == normalizedTicker
                && existingOrder.BidAmount == request.BidAmount
                && existingOrder.Volume == request.Volume
                && existingOrder.IsBuy == request.IsBuy
                && existingOrder.ServerId == request.ServerId;
        }

        private static string GetPriceCacheKey(string ticker) => $"price_{ticker}";
    }
}
