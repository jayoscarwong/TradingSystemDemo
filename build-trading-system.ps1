$ErrorActionPreference = "Stop"
$SolutionName = "TradingSystem"

Write-Host "1. Creating Solution and Projects..." -ForegroundColor Green
dotnet new sln -n $SolutionName
dotnet new classlib -n "$SolutionName.Domain"
dotnet new classlib -n "$SolutionName.Application"
dotnet new classlib -n "$SolutionName.Infrastructure"
dotnet new webapi -n "$SolutionName.Api"
dotnet new worker -n "$SolutionName.Worker"

Write-Host "2. Adding Projects to Solution..." -ForegroundColor Green
dotnet sln add "$SolutionName.Domain/$SolutionName.Domain.csproj"
dotnet sln add "$SolutionName.Application/$SolutionName.Application.csproj"
dotnet sln add "$SolutionName.Infrastructure/$SolutionName.Infrastructure.csproj"
dotnet sln add "$SolutionName.Api/$SolutionName.Api.csproj"
dotnet sln add "$SolutionName.Worker/$SolutionName.Worker.csproj"

Write-Host "3. Setting up Project References (Clean Architecture)..." -ForegroundColor Green
dotnet add "$SolutionName.Application/$SolutionName.Application.csproj" reference "$SolutionName.Domain/$SolutionName.Domain.csproj"
dotnet add "$SolutionName.Infrastructure/$SolutionName.Infrastructure.csproj" reference "$SolutionName.Application/$SolutionName.Application.csproj"
dotnet add "$SolutionName.Api/$SolutionName.Api.csproj" reference "$SolutionName.Application/$SolutionName.Application.csproj"
dotnet add "$SolutionName.Api/$SolutionName.Api.csproj" reference "$SolutionName.Infrastructure/$SolutionName.Infrastructure.csproj"
dotnet add "$SolutionName.Worker/$SolutionName.Worker.csproj" reference "$SolutionName.Application/$SolutionName.Application.csproj"
dotnet add "$SolutionName.Worker/$SolutionName.Worker.csproj" reference "$SolutionName.Infrastructure/$SolutionName.Infrastructure.csproj"

Write-Host "4. Installing NuGet Packages..." -ForegroundColor Green
dotnet add "$SolutionName.Api/$SolutionName.Api.csproj" package Microsoft.Extensions.Caching.StackExchangeRedis
dotnet add "$SolutionName.Api/$SolutionName.Api.csproj" package Microsoft.EntityFrameworkCore.Design
dotnet add "$SolutionName.Api/$SolutionName.Api.csproj" package Pomelo.EntityFrameworkCore.MySql
dotnet add "$SolutionName.Infrastructure/$SolutionName.Infrastructure.csproj" package Microsoft.EntityFrameworkCore
dotnet add "$SolutionName.Infrastructure/$SolutionName.Infrastructure.csproj" package Pomelo.EntityFrameworkCore.MySql
dotnet add "$SolutionName.Worker/$SolutionName.Worker.csproj" package Quartz.Extensions.Hosting
dotnet add "$SolutionName.Worker/$SolutionName.Worker.csproj" package MassTransit.RabbitMQ
dotnet add "$SolutionName.Worker/$SolutionName.Worker.csproj" package Pomelo.EntityFrameworkCore.MySql

Write-Host "5. Creating Directory Structures..." -ForegroundColor Green
New-Item -ItemType Directory -Force -Path "$SolutionName.Domain/Entities" | Out-Null
New-Item -ItemType Directory -Force -Path "$SolutionName.Application/Commands" | Out-Null
New-Item -ItemType Directory -Force -Path "$SolutionName.Infrastructure/Data" | Out-Null
New-Item -ItemType Directory -Force -Path "$SolutionName.Api/Controllers" | Out-Null
New-Item -ItemType Directory -Force -Path "$SolutionName.Worker/Jobs" | Out-Null
New-Item -ItemType Directory -Force -Path "$SolutionName.Worker/Services" | Out-Null

Write-Host "6. Generating Source Files..." -ForegroundColor Green

# --- DOMAIN LAYER ---
Set-Content -Path "$SolutionName.Domain/Entities/TradeOrder.cs" -Value @'
using System;
using System.ComponentModel.DataAnnotations;

namespace TradingSystem.Domain.Entities
{
    public class TradeOrder
    {
        public Guid Id { get; set; }
        public string StockTicker { get; set; }
        public decimal BidAmount { get; set; }
        public string ServerId { get; set; }
        
       
        public byte RowVersion { get; set; }
    }
}
'@

# --- APPLICATION LAYER ---
Set-Content -Path "$SolutionName.Application/Commands/FetchStockPriceCommand.cs" -Value @'
namespace TradingSystem.Application.Commands
{
    public record FetchStockPriceCommand
    {
        public string Ticker { get; init; }
        public string ServerId { get; init; }
    }
}
'@

# --- INFRASTRUCTURE LAYER ---
Set-Content -Path "$SolutionName.Infrastructure/Data/TradingDbContext.cs" -Value @'
using Microsoft.EntityFrameworkCore;
using TradingSystem.Domain.Entities;

namespace TradingSystem.Infrastructure.Data
{
    public class TradingDbContext : DbContext
    {
        public TradingDbContext(DbContextOptions<TradingDbContext> options) : base(options) { }

        public DbSet<TradeOrder> TradeOrders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TradeOrder>()
               .Property(p => p.RowVersion)
               .IsRowVersion();
        }
    }
}
'@

# --- API LAYER ---
Set-Content -Path "$SolutionName.Api/Program.cs" -Value @'
using Microsoft.EntityFrameworkCore;
using TradingSystem.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
    options.InstanceName = "TradingSystem_";
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<TradingDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();
app.Run();
'@

Set-Content -Path "$SolutionName.Api/Controllers/TradesController.cs" -Value @'
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Threading.Tasks;
using TradingSystem.Domain.Entities;

namespace TradingSystem.Api.Controllers
{
    [ApiController]
   ")]
    public class TradesController : ControllerBase
    {
        private readonly IDistributedCache _redisCache;

        public TradesController(IDistributedCache redisCache)
        {
            _redisCache = redisCache;
        }

        [HttpPost]
        public async Task<IActionResult> PlaceBid( TradeOrder order)
        {
            var serializedOrder = JsonSerializer.Serialize(order);
            var cacheKey = $"incoming_trades_{Guid.NewGuid()}";
            
            await _redisCache.SetStringAsync(cacheKey, serializedOrder);
            
            return Accepted(new { Message = "Bid received and buffered in Redis." });
        }
    }
}
'@

Set-Content -Path "$SolutionName.Api/appsettings.json" -Value @'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=tradingsystem;User=root;Password=rootpassword;"
  }
}
'@

# --- WORKER LAYER ---
Set-Content -Path "$SolutionName.Worker/Program.cs" -Value @'
using MassTransit;
using Quartz;
using TradingSystem.Worker.Jobs;
using TradingSystem.Worker.Services;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System;

var builder = Host.CreateDefaultBuilder(args);

builder.ConfigureServices((hostContext, services) =>
{
    var connectionString = hostContext.Configuration.GetConnectionString("DefaultConnection");
    
    services.AddDbContext<TradingDbContext>(options =>
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
    );

    services.AddTransient<TradePersistenceService>();

    services.AddMassTransit(x =>
    {
        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.Host("localhost", "/", h => {
                h.Username("guest");
                h.Password("guest");
            });
            cfg.ConfigureEndpoints(context);
        });
    });

    services.AddQuartz(q =>
    {
        q.UsePersistentStore(s =>
        {
            s.UseProperties = true;
            s.UseMySql(sql =>
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
           .WithCronSchedule("0 0 * * *?")); 
    });

    services.AddQuartzHostedService(options =>
    {
        options.WaitForJobsToComplete = true;
    });
});

var host = builder.Build();
host.Run();
'@

Set-Content -Path "$SolutionName.Worker/appsettings.json" -Value @'
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=tradingsystem;User=root;Password=rootpassword;"
  },
  "Quartz": {
    "quartz.scheduler.instanceName": "TradingSystemCluster",
    "quartz.scheduler.instanceId": "AUTO",
    "quartz.jobStore.type": "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz",
    "quartz.jobStore.driverDelegateType": "Quartz.Impl.AdoJobStore.MySQLDelegate, Quartz",
    "quartz.jobStore.dataSource": "default",
    "quartz.jobStore.clustered": "true",
    "quartz.serializer.type": "json"
  }
}
'@

Set-Content -Path "$SolutionName.Worker/Jobs/MasterOrchestratorJob.cs" -Value @'
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
'@

Set-Content -Path "$SolutionName.Worker/Jobs/SymbolDataPullJob.cs" -Value @'
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
            var serverId = context.JobDetail.JobDataMap.GetString("ServerId");
            
            await _publishEndpoint.Publish(new FetchStockPriceCommand 
            { 
                Ticker = "AAPL", 
                ServerId = serverId 
            });
        }
    }
}
'@

Set-Content -Path "$SolutionName.Worker/Services/TradePersistenceService.cs" -Value @'
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingSystem.Domain.Entities;
using TradingSystem.Infrastructure.Data;

namespace TradingSystem.Worker.Services
{
    public class TradePersistenceService
    {
        private readonly TradingDbContext _dbContext;

        public TradePersistenceService(TradingDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task UpdateTradeOrderSafeAsync(TradeOrder incomingTrade)
        {
            bool saved = false;
            while (!saved)
            {
                try
                {
                    var existingTrade = await _dbContext.TradeOrders
                       .SingleOrDefaultAsync(t => t.StockTicker == incomingTrade.StockTicker);

                    if (existingTrade!= null)
                    {
                        if(incomingTrade.BidAmount > existingTrade.BidAmount)
                        {
                            existingTrade.BidAmount = incomingTrade.BidAmount;
                        }
                    }
                    else
                    {
                        _dbContext.TradeOrders.Add(incomingTrade);
                    }

                    await _dbContext.SaveChangesAsync();
                    saved = true;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    foreach (var entry in ex.Entries)
                    {
                        if (entry.Entity is TradeOrder)
                        {
                            var databaseValues = await entry.GetDatabaseValuesAsync();
                            if (databaseValues!= null)
                            {
                                entry.OriginalValues.SetValues(databaseValues);
                            }
                            else
                            {
                                saved = true; // Row was deleted
                            }
                        }
                    }
                }
            }
        }
    }
}
'@

# --- INFRASTRUCTURE FILES ---
Set-Content -Path "docker-compose.yml" -Value @'
version: '3.8'
services:
  mysql:
    image: mysql:8.0
    environment:
      MYSQL_ROOT_PASSWORD: rootpassword
      MYSQL_DATABASE: tradingsystem
    ports:
      - "3306:3306"
    volumes:
      - mysql_data:/var/lib/mysql

  redis:
    image: redis:alpine
    ports:
      - "6379:6379"

  rabbitmq:
    image: rabbitmq:3-management
    ports:
      - "5672:5672"
      - "15672:15672"
    environment:
      RABBITMQ_DEFAULT_USER: guest
      RABBITMQ_DEFAULT_PASS: guest

volumes:
  mysql_data:
'@

Set-Content -Path "keda-rabbitmq-scaler.yaml" -Value @'
apiVersion: keda.sh/v1alpha1
kind: ScaledObject
metadata:
  name: rabbitmq-worker-scaler
  namespace: default
spec:
  scaleTargetRef:
    name: tradingsystem-worker-deployment
  minReplicaCount: 2
  maxReplicaCount: 15
  triggers:
  - type: rabbitmq
    metadata:
      queueName: fetch-stock-price-queue
      host: amqp://guest:guest@rabbitmq.default.svc.cluster.local:5672/
      mode: QueueLength
      value: "50"
'@

Write-Host "Done! The complete Clean Architecture solution has been generated with all files included." -ForegroundColor Cyan