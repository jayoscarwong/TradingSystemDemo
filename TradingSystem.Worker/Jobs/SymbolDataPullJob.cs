using System.Threading.Tasks;
using MassTransit;
using Quartz;
using TradingSystem.Application.Commands;

namespace TradingSystem.Worker.Jobs
{
    public class SymbolDataPullJob : IJob
    {
        private readonly IPublishEndpoint _publishEndpoint;

        public SymbolDataPullJob(IPublishEndpoint publishEndpoint)
        {
            _publishEndpoint = publishEndpoint;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            int serverId = context.JobDetail.JobDataMap.GetInt("ServerId");

            // FIX: Extract the Ticker dynamically from the JobDataMap
            string ticker = context.JobDetail.JobDataMap.GetString("Ticker") ?? "UNKNOWN";

            await _publishEndpoint.Publish(new FetchStockPriceCommand
            {
                Ticker = ticker,
                ServerId = serverId
            });
        }
    }
}