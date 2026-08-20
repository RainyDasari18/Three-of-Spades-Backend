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
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key missing.");
var frontend = builder.Configuration["Frontend:Origin"] ?? "http://localhost:5173";

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<RoomService>();
builder.Services.AddSingleton<LiveGameService>();
builder.Services.AddHostedService<DisconnectWorker>();
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

var auth = builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = "External";
}).AddCookie("External", o =>
{
    o.Cookie.Name = "tos.oauth";
    o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
}).AddJwtBearer(o =>
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

var googleId = builder.Configuration["Authentication:Google:ClientId"];
if (!string.IsNullOrWhiteSpace(googleId))
{
    auth.AddGoogle(o =>
    {
        o.ClientId = googleId;
        o.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"] ?? "";
        o.SignInScheme = "External";
        o.CallbackPath = "/signin-google";
        o.SaveTokens = true;
    });
}

var githubId = builder.Configuration["Authentication:GitHub:ClientId"];
if (!string.IsNullOrWhiteSpace(githubId))
{
    auth.AddGitHub(o =>
    {
        o.ClientId = githubId!;
        o.ClientSecret = builder.Configuration["Authentication:GitHub:ClientSecret"] ?? "";
        o.SignInScheme = "External";
        o.CallbackPath = "/signin-github";
        o.Scope.Add("user:email");
    });
}

builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddPolicy("app", p =>
    p.WithOrigins(frontend, "http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();
app.UseDomainErrors();
app.UseCors("app");
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
}

app.Run();
