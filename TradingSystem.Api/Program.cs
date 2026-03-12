using Microsoft.EntityFrameworkCore;
using Quartz;
using TradingSystem.Infrastructure.Data;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 1. Redis Configuration (For fast price reads)
var RedisSettings = builder.Configuration.GetSection("Redis");
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = RedisSettings["Configuration"];
    options.InstanceName = RedisSettings["InstanceName"];
});

// 2. MySQL Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<TradingDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
);

var rabbitMQSettings = builder.Configuration.GetSection("RabbitMQ");
string rabbitMQHost = rabbitMQSettings["Host"] ?? "tradingsystem-rabbitmq";
string rabbitMQUsername = rabbitMQSettings["Username"] ?? "guest";
string rabbitMQpassword = rabbitMQSettings["Password"] ?? "guest";

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<TradingSystem.Worker.Consumers.ProcessTradeConsumer>();
    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(rabbitMQHost, "/", h =>
        {
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


builder.Services.AddQuartz(q =>
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
    });
});

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthorization();
app.MapControllers();
app.Run();