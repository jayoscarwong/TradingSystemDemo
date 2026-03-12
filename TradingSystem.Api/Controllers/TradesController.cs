using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Threading.Tasks;
using TradingSystem.Domain.Entities;

namespace TradingSystem.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // <--- THIS IS THE FIX
    public class TradesController : ControllerBase
    {
        private readonly IDistributedCache _redisCache;

        public TradesController(IDistributedCache redisCache)
        {
            _redisCache = redisCache;
        }

        [HttpPost] // This will now route to POST /api/trades
        public async Task<IActionResult> PlaceBid(TradeOrder order)
        {
            var serializedOrder = JsonSerializer.Serialize(order);
            var cacheKey = $"incoming_trades_{Guid.NewGuid()}";

            await _redisCache.SetStringAsync(cacheKey, serializedOrder);

            return Accepted(new { Message = "Bid received and buffered in Redis." });
        }
    }
}