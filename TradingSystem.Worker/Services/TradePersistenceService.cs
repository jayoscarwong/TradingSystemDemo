using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Domain.Entities;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Worker.Services
{
    public class TradePersistenceService
    {
        private readonly TradingDbContext _dbContext;

        public TradePersistenceService(TradingDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task UpdateTradeOrderSafeAsync(TradeOrder incomingTrade)
        {
            bool saved = false;
            while (!saved)
            {
                try
                {
                    var existingTrade = await _dbContext.TradeOrders
                       .SingleOrDefaultAsync(t => t.StockTicker == incomingTrade.StockTicker);

                    if (existingTrade!= null)
                    {
                        if(incomingTrade.BidAmount > existingTrade.BidAmount)
                        {
                            existingTrade.BidAmount = incomingTrade.BidAmount;
                        }
                    }
                    else
                    {
                        _dbContext.TradeOrders.Add(incomingTrade);
                    }

                    await _dbContext.SaveChangesAsync();
                    saved = true;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    foreach (var entry in ex.Entries)
                    {
                        if (entry.Entity is TradeOrder)
                        {
                            var databaseValues = await entry.GetDatabaseValuesAsync();
                            if (databaseValues!= null)
                            {
                                entry.OriginalValues.SetValues(databaseValues);
                            }
                            else
                            {
                                saved = true; // Row was deleted
                            }
                        }
                    }
                }
            }
        }
    }
}
