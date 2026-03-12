using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using TradingSystem.Infrastructure.Data;
using TradingSystem.Worker.Jobs;
using TradingSystem.Worker.Services;
using TradingSystem.Infrastructure.Services;


var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((hostContext, services) =>
{
    var configuration = hostContext.Configuration;
    var connectionString = configuration.GetConnectionString("DefaultConnection");

    services.AddDbContext<TradingDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
    );
    // Register Application Services
    services.AddTransient<TradePersistenceService>();
    services.AddTransient<StockPriceService>();
    services.AddTransient<SymbolDataPullJob>();

    // Register the Quartz Listener
    services.AddSingleton<JobExecutionHistoryListener>();


    var rabbitMQSettings = configuration.GetSection("RabbitMQ");
    string rabbitMQHost = rabbitMQSettings["Host"] ?? "tradingsystem-rabbitmq";
    string rabbitMQUsername = rabbitMQSettings["Username"] ?? "guest";
    string rabbitMQpassword = rabbitMQSettings["Password"] ?? "guest";

    services.AddMassTransit(x =>
    {
        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(rabbitMQHost, "/", h =>
            {
                h.Username(rabbitMQUsername);
                h.Password(rabbitMQpassword);
            });
            cfg.ConfigureEndpoints(context);
        });
    });


    services.AddQuartz(q =>
    {
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
          .WithCronSchedule("0 0 * * * ?")); // Runs every hour to check for new/disabled servers
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