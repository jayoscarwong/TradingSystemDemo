using System.Collections.Generic;
using System.Threading.Tasks;
using Quartz;

namespace TradingSystem.Worker.Jobs
{
   
    public class MasterOrchestratorJob : IJob
    {
        public async Task Execute(IJobExecutionContext context)
        {
            var scheduler = context.Scheduler;
            
            var activeServers = new List<string> { "ServerA", "ServerB" }; 

            foreach (var serverId in activeServers)
            {
                var jobKey = new JobKey($"DataPullJob-{serverId}", "DataPullGroup");
                
                if (!await scheduler.CheckExists(jobKey))
                {
                    var job = JobBuilder.Create<SymbolDataPullJob>()
                       .WithIdentity(jobKey)
                       .UsingJobData("ServerId", serverId)
                       .Build();

                    var trigger = TriggerBuilder.Create()
                       .WithIdentity($"Trigger-{serverId}", "DataPullGroup")
                       .StartNow()
                       .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).RepeatForever())
                       .Build();

                    await scheduler.ScheduleJob(job, trigger);
                }
            }
        }
    }
}
