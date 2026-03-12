using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using TradingSystem.Infrastructure.Data;
using TradingSystem.Worker.Jobs;
using TradingSystem.Worker.Services;
using TradingSystem.Infrastructure.Services;
using TradingSystem.Application.Interfaces;


var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((hostContext, services) =>
{
    var configuration = hostContext.Configuration;
    var connectionString = configuration.GetConnectionString("DefaultConnection");

    services.AddDbContext<TradingDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
    );
    services.AddTransient<TradePersistenceService>();
    // Make sure your IStockPriceService is registered so the Consumer can use it
    services.AddTransient<TradingSystem.Application.Interfaces.IStockPriceService, TradingSystem.Infrastructure.Services.StockPriceService>();
    services.AddTransient<SymbolDataPullJob>();

    // Register the Quartz Listener
    //services.AddSingleton<JobExecutionHistoryListener>();
    services.AddTransient<TradingSystem.Worker.Jobs.JobExecutionHistoryListener>();

    var rabbitMQSettings = configuration.GetSection("RabbitMQ");
    string rabbitMQHost = rabbitMQSettings["Host"] ?? "tradingsystem-rabbitmq";
    string rabbitMQUsername = rabbitMQSettings["Username"] ?? "guest";
    string rabbitMQpassword = rabbitMQSettings["Password"] ?? "guest";

    services.AddMassTransit(x =>
    {
        x.AddConsumer<TradingSystem.Worker.Consumers.ProcessTradeConsumer>();
        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(rabbitMQHost, "/", h => {
                h.Username(rabbitMQUsername);
                h.Password(rabbitMQpassword);
            });

            // ADDED: Bind the Consumer to the KEDA queue
            cfg.ReceiveEndpoint("process-trade-queue", e =>
            {
                e.ConfigureConsumer<TradingSystem.Worker.Consumers.ProcessTradeConsumer>(context);
            });

            cfg.ConfigureEndpoints(context);
        });
    });


    services.AddQuartz(q =>
    {
        // --- ADD THIS LINE so Quartz logs executions to the database ---
        q.AddJobListener<TradingSystem.Worker.Jobs.JobExecutionHistoryListener>(Quartz.Impl.Matchers.GroupMatcher<JobKey>.AnyGroup());

        q.UsePersistentStore(s =>
        {
            s.UseProperties = true;
            s.UseMySqlConnector(sql =>
            {
                sql.ConnectionString = connectionString;
                sql.TablePrefix = "QRTZ_";
            });
            s.UseSystemTextJsonSerializer();
            s.UseClustering(c =>
            {
                c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                c.CheckinInterval = TimeSpan.FromSeconds(10);
            });
        });


        // Register the Master Orchestrator Job
        var jobKey = new JobKey("MasterOrchestratorJob");
        q.AddJob<MasterOrchestratorJob>(opts => opts.WithIdentity(jobKey));
        q.AddTrigger(opts => opts
          .ForJob(jobKey)
          .WithIdentity("MasterOrchestratorTrigger")
          .StartNow() // ADDED: Forces the job to run immediately on container start
          .WithCronSchedule("0 0/5 * * * ?")); // Runs every 5 minutes
    });

    services.AddQuartzHostedService(options =>
    {
        options.WaitForJobsToComplete = true;
    });

});

var host = builder.Build();
// Register the job listener globally before running
var schedulerFactory = host.Services.GetRequiredService<ISchedulerFactory>();
var scheduler = schedulerFactory.GetScheduler().GetAwaiter().GetResult();
var listener = host.Services.GetRequiredService<JobExecutionHistoryListener>();
scheduler.ListenerManager.AddJobListener(listener);
host.Run();