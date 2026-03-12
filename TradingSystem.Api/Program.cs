using Microsoft.EntityFrameworkCore;
using TradingSystem.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();



var RedisSettings = builder.Configuration.GetSection("Redis");
// Pull Redis configuration from appsettings.json
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = RedisSettings["Configuration"];
    options.InstanceName = RedisSettings["InstanceName"];
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found in the configuration.");
}
builder.Services.AddDbContext<TradingDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
);

var app = builder.Build();

//if (app.Environment.IsDevelopment())
//{
    app.UseSwagger();
    app.UseSwaggerUI();
//}

app.UseAuthorization();
app.MapControllers();
app.Run();