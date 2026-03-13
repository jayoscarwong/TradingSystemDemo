using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using TradingSystem.Application.Commands;
using TradingSystem.Domain.Entities;
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

        [HttpPost]
        public async Task<IActionResult> PlaceBid([FromBody] PlaceBidRequest request)
        {
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
                return BuildDuplicateRequestResponse(existingOrder, request, normalizedTicker);
            }

            var utcNow = DateTime.UtcNow;
            var order = new TradeOrder
            {
                Id = request.OrderId,
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

                return BuildDuplicateRequestResponse(existingOrder, request, normalizedTicker);
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

        [HttpGet("price/{ticker}")]
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

        private IActionResult BuildDuplicateRequestResponse(TradeOrder existingOrder, PlaceBidRequest request, string normalizedTicker)
        {
            if (!MatchesExistingOrder(existingOrder, request, normalizedTicker))
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

        private static bool MatchesExistingOrder(TradeOrder existingOrder, PlaceBidRequest request, string normalizedTicker)
        {
            return existingOrder.StockTicker == normalizedTicker
                && existingOrder.BidAmount == request.BidAmount
                && existingOrder.Volume == request.Volume
                && existingOrder.IsBuy == request.IsBuy
                && existingOrder.ServerId == request.ServerId;
        }

        private static string GetPriceCacheKey(string ticker) => $"price_{ticker}";
    }
}
