using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Application.Services
{
    public class StockPriceService
    {
        private readonly TradingDbContext _dbContext;

        public StockPriceService(TradingDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task UpdateStockPriceAsync(string ticker, string serverId, decimal orderPrice, decimal orderVolume)
        {
            var stock = await _dbContext.StockPrices
                .FirstOrDefaultAsync(s => s.Ticker == ticker && s.ServerId == serverId);

            if (stock != null)
            {
                if (orderPrice > stock.CurrentPrice)
                {
                    stock.CurrentPrice += (orderVolume * 0.01m);
                }
                else
                {
                    stock.CurrentPrice -= (orderVolume * 0.01m);
                }

                stock.BuyVolume += orderVolume;
                stock.LastUpdatedAt = System.DateTime.UtcNow;

                _dbContext.StockPrices.Update(stock);
                await _dbContext.SaveChangesAsync();
            }
        }
    }
}