using System.Threading.Tasks;
using Quartz;
using TradingSystem.Application.Services;

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
            var orderPrice = context.JobDetail.JobDataMap.GetDecimal("orderPrice");
            var orderVolume = context.JobDetail.JobDataMap.GetDecimal("orderVolume");

            // ServerId is passed down to guarantee isolation across servers
            await _stockPriceService.UpdateStockPriceAsync(ticker, serverId, orderPrice, orderVolume);
        }
    }
}