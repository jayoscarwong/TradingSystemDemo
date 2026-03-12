using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Threading.Tasks;
using TradingSystem.Domain.Entities;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // <--- THIS IS THE FIX
    public class TradesController : ControllerBase
    {
        private readonly IDistributedCache _redisCache;
        private readonly TradingDbContext _dbContext;

        public TradesController(IDistributedCache redisCache, TradingDbContext dbContext)
        {
            _redisCache = redisCache;
            _dbContext = dbContext;
        }

        [HttpPost]
        public async Task<IActionResult> PlaceBid(TradeOrder order)
        {
            // 1. Buffer to Redis (Original Logic)
            var serializedOrder = JsonSerializer.Serialize(order);
            var cacheKey = $"incoming_trades_{Guid.NewGuid()}";
            await _redisCache.SetStringAsync(cacheKey, serializedOrder);

            // 2. FIX: Save directly to MySQL
            var existingTrade = await _dbContext.TradeOrders.FindAsync(order.Id);
            if (existingTrade == null)
            {
                _dbContext.TradeOrders.Add(order);
            }
            else
            {
                // Upsert logic if the ID already exists
                existingTrade.BidAmount = order.BidAmount;
                existingTrade.ServerId = order.ServerId;
                _dbContext.TradeOrders.Update(existingTrade);
            }

            await _dbContext.SaveChangesAsync();

            return Accepted(new { Message = "Bid received, buffered in Redis, and saved to MySQL." });
        }
    }
}