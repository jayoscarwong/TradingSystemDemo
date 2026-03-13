using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TradingSystem.Auth.Options;
using TradingSystem.Auth.Services;
using TradingSystem.Domain.Security;
using TradingSystem.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "TradingSystem.Auth", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste a JWT bearer token here."
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

builder.Services.Configure<AuthenticationSettings>(builder.Configuration.GetSection("Authentication"));

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

builder.Services.AddDbContext<TradingDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

var redisSettings = builder.Configuration.GetSection("Redis");
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisSettings["Configuration"];
    options.InstanceName = redisSettings["InstanceName"];
});

builder.Services.AddScoped<PasswordHashService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<TradeSessionCacheService>();
builder.Services.AddHostedService<IdentityBootstrapService>();

var authenticationSettings = builder.Configuration.GetSection("Authentication").Get<AuthenticationSettings>()
    ?? throw new InvalidOperationException("Authentication settings are missing.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authenticationSettings.SigningKey));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = authenticationSettings.Issuer,
            ValidAudience = authenticationSettings.Audience,
            IssuerSigningKey = signingKey,
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var username = context.Principal?.Identity?.Name;
                var sessionId = context.Principal?.FindFirst("sid")?.Value;
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(sessionId))
                {
                    context.Fail("Session claims are missing.");
                    return;
                }

                var sessionCache = context.HttpContext.RequestServices.GetRequiredService<TradeSessionCacheService>();
                var isValid = await sessionCache.IsSessionValidAsync(username, sessionId, context.HttpContext.RequestAborted);
                if (!isValid)
                {
                    context.Fail("Session expired or logged out.");
                }
            }
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.AccountsManage, policy =>
        policy.RequireClaim("permission", PermissionCodes.AccountsManage));
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
