using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ThreeOfSpades.Api.Auth;
using ThreeOfSpades.Api.Data;
using ThreeOfSpades.Api.Hosting;
using ThreeOfSpades.Api.Hubs;
using ThreeOfSpades.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var jwtKey = builder.Configuration["Jwt:Key"]?.Trim();
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key must be set (at least 32 characters). Use Jwt__Key in production.");
if (!builder.Environment.IsDevelopment() &&
    jwtKey == "dev-only-change-me-three-of-spades-super-secret-key!")
    throw new InvalidOperationException("Do not use the development Jwt:Key in production.");
var frontend = builder.Configuration["Frontend:Origin"] ?? "http://localhost:5173";

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<RoomService>();
builder.Services.AddSingleton<LiveGameService>();
builder.Services.AddHostedService<DisconnectWorker>();
builder.Services.AddHostedService<BotWorker>();
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddSignalR().AddJsonProtocol(o =>
{
    o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var access = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(access) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs/game"))
                    ctx.Token = access;
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddPolicy("app", p =>
    p.WithOrigins(frontend, "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();
app.UseDomainErrors();
app.UseCors("app");
app.Use(async (ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        ctx.Response.Headers.CacheControl = "no-store, no-cache, private";
        ctx.Response.Headers.Pragma = "no-cache";
        return Task.CompletedTask;
    });
    await next();
});
app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<GameHub>("/hubs/game");
app.MapGet("/health", () => Results.Ok(new { ok = true, service = "three-of-spades" }));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await db.Users
        .Where(u => !u.IsBot && u.Email.EndsWith("@spades.local"))
        .ExecuteUpdateAsync(u => u.SetProperty(x => x.IsBot, true));
}

app.Run();
