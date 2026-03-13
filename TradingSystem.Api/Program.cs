using MassTransit;
using Microsoft.EntityFrameworkCore;
using Quartz;
using TradingSystem.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var redisSettings = builder.Configuration.GetSection("Redis");
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisSettings["Configuration"];
    options.InstanceName = redisSettings["InstanceName"];
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");
builder.Services.AddDbContext<TradingDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
);

var rabbitMQSettings = builder.Configuration.GetSection("RabbitMQ");
var rabbitMQHost = rabbitMQSettings["Host"] ?? "tradingsystem-rabbitmq";
var rabbitMQUsername = rabbitMQSettings["Username"] ?? "guest";
var rabbitMQpassword = rabbitMQSettings["Password"] ?? "guest";

builder.Services.AddMassTransit(x =>
{
    x.UsingRabbitMq((_, cfg) =>
    {
        cfg.Host(rabbitMQHost, "/", h =>
        {
            h.Username(rabbitMQUsername);
            h.Password(rabbitMQpassword);
        });
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
