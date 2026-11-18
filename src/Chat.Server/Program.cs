using System.Text;
using Chat.Server.Auth;
using Chat.Server.Hubs;
using Chat.Server.Services;
using Chat.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---------- Configuration ----------
var redisConnection = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379,abortConnect=false";
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey must be set and at least 32 characters.");
builder.Services.AddSingleton(jwt);

// ---------- Redis (shared by backplane, history and presence) ----------
var mux = await ConnectionMultiplexer.ConnectAsync(redisConnection);
builder.Services.AddSingleton<IConnectionMultiplexer>(mux);
builder.Services.AddSingleton<IMessageStore, RedisMessageStore>();
builder.Services.AddSingleton<IPresenceTracker, RedisPresenceTracker>();
builder.Services.AddSingleton<TokenService>();

// ---------- Auth ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        // WebSockets can't send an Authorization header from every platform,
        // so SignalR passes the token in the query string for the hub route.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(ChatRoutes.Hub))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// ---------- SignalR + Redis backplane ----------
builder.Services
    .AddSignalR(o =>
    {
        o.EnableDetailedErrors = builder.Environment.IsDevelopment();
        o.MaximumReceiveMessageSize = 64 * 1024;
        o.KeepAliveInterval = TimeSpan.FromSeconds(15);
        o.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    })
    .AddStackExchangeRedis(redisConnection, o =>
    {
        o.Configuration.ChannelPrefix = RedisChannel.Literal("chat");
    });

builder.Services.AddHealthChecks().AddRedis(mux, name: "redis");

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Text($"Chat server running on {Environment.MachineName}"));
app.MapHealthChecks(ChatRoutes.Health);

// Development-grade token endpoint: issues a JWT for any user name.
// Replace with your real identity provider (Entra ID, Auth0, Keycloak, ...) in production.
app.MapPost(ChatRoutes.Token, (TokenRequest req, TokenService tokens) =>
{
    var name = req.UserName?.Trim();
    if (string.IsNullOrEmpty(name) || name.Length > 32)
        return Results.BadRequest("UserName must be 1-32 characters.");
    return Results.Ok(tokens.Issue(name));
});

app.MapHub<ChatHub>(ChatRoutes.Hub).RequireAuthorization();

app.Run();
