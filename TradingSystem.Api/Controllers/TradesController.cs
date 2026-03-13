using MassTransit;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Distributed;
using System;
using System.Text.Json;
using System.Text.Json;
using System.Threading.Tasks;
using TradingSystem.Api.DTOs;
using TradingSystem.Application.Commands;
using TradingSystem.Application.Commands;
using TradingSystem.Domain.Entities;
using TradingSystem.Domain.Entities;
using TradingSystem.Infrastructure.Data;
using TradingSystem.Infrastructure.Data;



namespace TradingSystem.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")] 
    public class TradesController : ControllerBase
    {
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
            // 1. Idempotency Check: Did we already process this exact order?
            var existingOrder = await _dbContext.TradeOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.OrderId);
            if (existingOrder != null)
            {
                return Ok(new { Message = "Order already received and is being processed." });
            }

            var order = new TradeOrder
            {
                Id = request.OrderId, // Use client's GUID
                StockTicker = request.StockTicker,
                BidAmount = request.BidAmount,
                Volume = request.Volume,
                IsBuy = request.IsBuy,
                ServerId = request.ServerId,
                IsProcessed = false
            };

            _dbContext.TradeOrders.Add(order);
            await _dbContext.SaveChangesAsync();

            // 2. Publish to RabbitMQ
            await _publishEndpoint.Publish(new ProcessTradeCommand { OrderId = order.Id });

            return Accepted(new { Message = "Order received and queued for real-time processing." });
        }

        [HttpGet("price/{ticker}")]
        public async Task<IActionResult> GetRealTimePrice(string ticker)
        {
            var cacheKey = $"price_{ticker}";
            var cachedPrice = await _redisCache.GetStringAsync(cacheKey);

            if (!string.IsNullOrEmpty(cachedPrice))
            {
                return Ok(JsonSerializer.Deserialize<StockPrice>(cachedPrice));
            }

            var stockPrice = await _dbContext.StockPrices.FirstOrDefaultAsync(s => s.Ticker == ticker);
            if (stockPrice == null) return NotFound(new { Message = "Stock not found." });

            await _redisCache.SetStringAsync(cacheKey, JsonSerializer.Serialize(stockPrice));

            return Ok(stockPrice);
        }



        //[HttpPost]
        //public async Task<IActionResult> PlaceBid(TradeOrder order)
        //{
        //    // 1. Buffer to Redis (Original Logic)
        //    var serializedOrder = JsonSerializer.Serialize(order);
        //    var cacheKey = $"incoming_trades_{Guid.NewGuid()}";
        //    await _redisCache.SetStringAsync(cacheKey, serializedOrder);

        //    // 2. FIX: Save directly to MySQL
        //    var existingTrade = await _dbContext.TradeOrders.FindAsync(order.Id);
        //    if (existingTrade == null)
        //    {
        //        _dbContext.TradeOrders.Add(order);
        //    }
        //    else
        //    {
        //        // Upsert logic if the ID already exists
        //        existingTrade.BidAmount = order.BidAmount;
        //        existingTrade.ServerId = order.ServerId;
        //        _dbContext.TradeOrders.Update(existingTrade);
        //    }

        //    await _dbContext.SaveChangesAsync();

        //    return Accepted(new { Message = "Bid received, buffered in Redis, and saved to MySQL." });
        //}

        //[HttpPost]
        //public async Task<IActionResult> PlaceBid(TradeOrder order)
        //{
        //    order.IsProcessed = false;
        //    _dbContext.TradeOrders.Add(order);
        //    await _dbContext.SaveChangesAsync();
        //    // RabbitMQ handles the buffering now!
        //    await _publishEndpoint.Publish(new ProcessTradeCommand { OrderId = order.Id });
        //    return Accepted(new { Message = "Order received and queued for real-time processing." });
        //}


        //[HttpGet("price/{ticker}")]
        //public async Task<IActionResult> GetRealTimePrice(string ticker)
        //{
        //    var cacheKey = $"price_{ticker}";
        //    var cachedPrice = await _redisCache.GetStringAsync(cacheKey);

        //    // 1. Check Redis first (Super Fast)
        //    if (!string.IsNullOrEmpty(cachedPrice))
        //    {
        //        var priceData = JsonSerializer.Deserialize<StockPrice>(cachedPrice);
        //        return Ok(priceData);
        //    }

        //    // 2. Fallback to MySQL if Redis is empty
        //    var stockPrice = await _dbContext.StockPrices.FirstOrDefaultAsync(s => s.Ticker == ticker);
        //    if (stockPrice == null) return NotFound(new { Message = "Stock not found." });
        //    // 3. Update Redis for the next caller
        //    await _redisCache.SetStringAsync(cacheKey, JsonSerializer.Serialize(stockPrice));
        //    return Ok(stockPrice);
        //}
    }
}