using System.Threading.Tasks;
using Quartz;
using TradingSystem.Application.Interfaces; 

namespace TradingSystem.Worker.Jobs
{
    [DisallowConcurrentExecution]
    public class StockPriceUpdateJob : IJob
    {
        private readonly IStockPriceService _stockPriceService; 

        public StockPriceUpdateJob(IStockPriceService stockPriceService)
        {
            _stockPriceService = stockPriceService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var ticker = context.JobDetail.JobDataMap.GetString("ticker");
            var serverId = context.JobDetail.JobDataMap.GetString("ServerId");

            var orderPriceStr = context.JobDetail.JobDataMap.GetString("orderPrice");
            var orderVolumeStr = context.JobDetail.JobDataMap.GetString("orderVolume");

            decimal.TryParse(orderPriceStr, out decimal orderPrice);
            decimal.TryParse(orderVolumeStr, out decimal orderVolume);

            await _stockPriceService.UpdateStockPriceAsync(ticker, serverId, orderPrice, orderVolume);
        }
    }
}