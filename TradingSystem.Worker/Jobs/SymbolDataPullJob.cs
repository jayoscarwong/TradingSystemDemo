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
            var serverId = context.JobDetail.JobDataMap.GetInt("ServerId");

            await _publishEndpoint.Publish(new FetchStockPriceCommand 
            { 
                Ticker = "AAPL", 
                ServerId = serverId 
            });
        }
    }
}
