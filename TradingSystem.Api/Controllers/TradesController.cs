using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Threading.Tasks;
using TradingSystem.Domain.Entities;
using TradingSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading.Tasks;
using MassTransit;
using TradingSystem.Application.Commands;

namespace TradingSystem.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // <--- THIS IS THE FIX
    public class TradesController : ControllerBase
    {
        private readonly IDistributedCache _redisCache;
        private readonly TradingDbContext _dbContext;
        private readonly IPublishEndpoint _publishEndpoint;


        public TradesController(IDistributedCache redisCache, TradingDbContext dbContext, IPublishEndpoint publishEndpoint)
        {
            _redisCache = redisCache;
            _dbContext = dbContext;
            _publishEndpoint = publishEndpoint;
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

        [HttpPost]
        public async Task<IActionResult> PlaceBid2(TradeOrder order)
        {
            // 1. Ensure order is unprocessed initially
            order.IsProcessed = false;

            // 2. Insert safely into MySQL (No locks on the StockPrice table!)
            _dbContext.TradeOrders.Add(order);
            await _dbContext.SaveChangesAsync();

            // 3. Publish real-time event to RabbitMQ
            await _publishEndpoint.Publish(new ProcessTradeCommand { OrderId = order.Id });

            return Accepted(new { Message = "Bid received and queued for real-time processing." });
        }


        //[HttpGet("price/{ticker}/{serverId}")]
        //public async Task<IActionResult> GetRealTimePrice(string ticker, string serverId, [FromServices] TradingDbContext dbContext)
        //{
        //    var stockPrice = await dbContext.StockPrices.FirstOrDefaultAsync(s => s.Ticker == ticker && s.ServerId == serverId);

        //    if (stockPrice == null)
        //    {
        //        return NotFound(new { Message = $"No active market data found for {ticker} on {serverId}." });
        //    }

        //    return Ok(new
        //    {
        //        Ticker = stockPrice.Ticker,
        //        Server = stockPrice.ServerId,
        //        CurrentPrice = stockPrice.CurrentPrice,
        //        TotalVolume = stockPrice.BuyVolume,
        //        LastUpdated = stockPrice.LastUpdatedAt
        //    });
        //}
    }
}