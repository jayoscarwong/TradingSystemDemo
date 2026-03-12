using System.Threading.Tasks;

namespace TradingSystem.Application.Interfaces
{
    public interface IStockPriceService
    {
        Task UpdateStockPriceAsync(string ticker, string serverId, decimal orderPrice, decimal orderVolume);
    }
}