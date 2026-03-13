using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using TradingSystem.Infrastructure.Data;
using TradingSystem.Infrastructure.Services;
using TradingSystem.Worker.Jobs;
using TradingSystem.Worker.Services;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((hostContext, services) =>
{
    var configuration = hostContext.Configuration;
    var connectionString = configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

    services.AddDbContext<TradingDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
    );
    services.AddTransient<TradePersistenceService>();
    services.AddTransient<TradeExecutionService>();
    services.AddTransient<MasterOrchestratorJob>();
    services.AddTransient<SymbolDataPullJob>();
    services.AddTransient<JobExecutionHistoryListener>();
    services.AddScoped<ScheduledTaskQuartzService>();
    services.AddHostedService<ScheduledTaskBootstrapService>();

    var redisSettings = configuration.GetSection("Redis");
    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisSettings["Configuration"];
        options.InstanceName = redisSettings["InstanceName"];
    });

    var rabbitMQSettings = configuration.GetSection("RabbitMQ");
    var rabbitMQHost = rabbitMQSettings["Host"] ?? "tradingsystem-rabbitmq";
    var rabbitMQUsername = rabbitMQSettings["Username"] ?? "guest";
    var rabbitMQpassword = rabbitMQSettings["Password"] ?? "guest";

    services.AddMassTransit(x =>
    {
        x.AddConsumer<TradingSystem.Worker.Consumers.ProcessTradeConsumer>();
        x.AddConsumer<TradingSystem.Worker.Consumers.FetchStockPriceConsumer>();

        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host(rabbitMQHost, "/", h =>
            {
                h.Username(rabbitMQUsername);
                h.Password(rabbitMQpassword);
            });

            cfg.ReceiveEndpoint("process-trade-queue", e =>
            {
                e.ConfigureConsumer<TradingSystem.Worker.Consumers.ProcessTradeConsumer>(context);
            });

            cfg.ReceiveEndpoint("fetch-stock-price-queue", e =>
            {
                e.ConfigureConsumer<TradingSystem.Worker.Consumers.FetchStockPriceConsumer>(context);
            });
        });
    });

    services.AddQuartz(q =>
    {
        q.AddJobListener<JobExecutionHistoryListener>(Quartz.Impl.Matchers.GroupMatcher<JobKey>.AnyGroup());

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
    });

    services.AddQuartzHostedService(options =>
    {
        options.WaitForJobsToComplete = true;
    });
});

var host = builder.Build();
host.Run();
