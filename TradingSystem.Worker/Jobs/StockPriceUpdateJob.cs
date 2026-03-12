using System.Threading.Tasks;
using Quartz;
using TradingSystem.Infrastructure.Services;

namespace TradingSystem.Worker.Jobs
{
    [DisallowConcurrentExecution] // Prevents overlapping runs if the job runs longer than its interval
    public class StockPriceUpdateJob : IJob
    {
        private readonly StockPriceService _stockPriceService;

        public StockPriceUpdateJob(StockPriceService stockPriceService)
        {
            _stockPriceService = stockPriceService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var ticker = context.JobDetail.JobDataMap.GetString("ticker");
            var serverId = context.JobDetail.JobDataMap.GetString("ServerId");

            // FIX: Quartz doesn't have GetDecimal, so we get strings and parse them
            var orderPriceStr = context.JobDetail.JobDataMap.GetString("orderPrice");
            var orderVolumeStr = context.JobDetail.JobDataMap.GetString("orderVolume");

            decimal.TryParse(orderPriceStr, out decimal orderPrice);
            decimal.TryParse(orderVolumeStr, out decimal orderVolume);

            await _stockPriceService.UpdateStockPriceAsync(ticker, serverId, orderPrice, orderVolume);
        }
    }
}