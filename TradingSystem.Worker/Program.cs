using MassTransit;
using Quartz;
using TradingSystem.Worker.Jobs;
using TradingSystem.Worker.Services;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Infrastructure.Data;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((hostContext, services) =>
{
    var configuration = hostContext.Configuration;
    var connectionString = configuration.GetConnectionString("DefaultConnection");

    services.AddDbContext<TradingDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
    );

    services.AddTransient<TradePersistenceService>();
    // Register your dynamic job so Quartz can inject the MassTransit endpoint
    services.AddTransient<SymbolDataPullJob>();

    var rabbitMQSettings =configuration.GetSection("RabbitMQ");
    string rabbitMQHost = rabbitMQSettings["Host"] ?? string.Empty;
    string rabbitMQUsername = rabbitMQSettings["Username"] ?? string.Empty;
    string rabbitMQpassword = rabbitMQSettings["Password"] ?? string.Empty;

    services.AddMassTransit(x =>
    {
        x.UsingRabbitMq((context, cfg) =>
        {
            // Pull exact string values from appsettings.json
            var host = rabbitMQHost ?? "tradingsystem-rabbitmq";
            var user = rabbitMQUsername ?? "guest";
            var pass = rabbitMQpassword ?? "guest";

            cfg.Host(host, "/", h => {
                h.Username(user);
                h.Password(pass);
            });

            cfg.ConfigureEndpoints(context);
        });
    });

    services.AddQuartz(q =>
    {
        q.UsePersistentStore(s =>
        {
            s.UseProperties = true;

            // THE FIX: Change this from s.UseMySql to s.UseMySqlConnector
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


        var jobKey = new JobKey("MasterOrchestratorJob");
        q.AddJob<MasterOrchestratorJob>(opts => opts.WithIdentity(jobKey));
        q.AddTrigger(opts => opts
          .ForJob(jobKey)
          .WithIdentity("MasterOrchestratorTrigger")
          .WithCronSchedule("0 0 * * * ?"));
    });

    services.AddQuartzHostedService(options =>
    {
        options.WaitForJobsToComplete = true;
    });
});

var host = builder.Build();
host.Run();