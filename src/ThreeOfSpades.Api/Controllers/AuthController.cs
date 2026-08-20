using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ThreeOfSpades.Api.Auth;
using ThreeOfSpades.Api.Contracts;
using ThreeOfSpades.Api.Data;
using ThreeOfSpades.Api.Domain;

namespace ThreeOfSpades.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, JwtTokenService jwt, IConfiguration config) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.UserName))
            return BadRequest("Email, password, and userName are required.");
        if (req.UserName.Length is < 2 or > 24) return BadRequest("UserName must be 2–24 characters.");
        if (await db.Users.AnyAsync(u => u.Email == req.Email.Trim().ToLowerInvariant(), ct))
            return Conflict("Email already registered.");
        if (await db.Users.AnyAsync(u => u.UserName == req.UserName.Trim(), ct))
            return Conflict("UserName is taken.");

        var user = new User
        {
            Email = req.Email.Trim().ToLowerInvariant(),
            UserName = req.UserName.Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password)
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return Ok(ToAuth(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req, CancellationToken ct)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email && !u.IsBot, ct);
        if (user?.PasswordHash is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized("Invalid email or password.");
        return Ok(ToAuth(user));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<AuthResponse>> Me(CancellationToken ct)
    {
        var user = await db.Users.FindAsync([JwtTokenService.UserId(User)], ct);
        if (user is null) return Unauthorized();
        return Ok(ToAuth(user));
    }

    [Authorize]
    [HttpPut("username")]
    public async Task<ActionResult<AuthResponse>> SetUserName(SetUserNameRequest req, CancellationToken ct)
    {
        var name = req.UserName.Trim();
        if (name.Length is < 2 or > 24) return BadRequest("UserName must be 2–24 characters.");
        var user = await db.Users.FindAsync([JwtTokenService.UserId(User)], ct);
        if (user is null) return Unauthorized();
        if (await db.Users.AnyAsync(u => u.UserName == name && u.Id != user.Id, ct))
            return Conflict("UserName is taken.");
        user.UserName = name;
        await db.SaveChangesAsync(ct);
        return Ok(ToAuth(user));
    }

    [HttpGet("google")]
    public IActionResult Google()
    {
        if (string.IsNullOrWhiteSpace(config["Authentication:Google:ClientId"]))
            return BadRequest("Google OAuth is not configured.");
        var props = new AuthenticationProperties { RedirectUri = "/api/auth/google/callback" };
        return Challenge(props, "Google");
    }

    [HttpGet("google/callback")]
    public Task<IActionResult> GoogleCallback(CancellationToken ct) => ExternalCallback("Google", "google", ct);

    [HttpGet("github")]
    public IActionResult GitHub()
    {
        if (string.IsNullOrWhiteSpace(config["Authentication:GitHub:ClientId"]))
            return BadRequest("GitHub OAuth is not configured.");
        var props = new AuthenticationProperties { RedirectUri = "/api/auth/github/callback" };
        return Challenge(props, "GitHub");
    }

    [HttpGet("github/callback")]
    public Task<IActionResult> GitHubCallback(CancellationToken ct) => ExternalCallback("GitHub", "github", ct);

    private async Task<IActionResult> ExternalCallback(string scheme, string provider, CancellationToken ct)
    {
        var result = await HttpContext.AuthenticateAsync("External");
        if (!result.Succeeded || result.Principal is null)
            return Unauthorized("OAuth failed.");

        var principal = result.Principal;
        var externalId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var email = (principal.FindFirstValue(ClaimTypes.Email) ?? $"{externalId}@{provider}.oauth").ToLowerInvariant();
        var suggested = principal.FindFirstValue(ClaimTypes.Name)
                        ?? principal.FindFirstValue("urn:github:login")
                        ?? email.Split('@')[0];

        User? user = provider == "google"
            ? await db.Users.FirstOrDefaultAsync(u => u.GoogleId == externalId, ct)
            : await db.Users.FirstOrDefaultAsync(u => u.GitHubId == externalId, ct);
        user ??= await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            var unique = await UniqueUserName(suggested, ct);
            user = new User { Email = email, UserName = unique };
            if (provider == "google") user.GoogleId = externalId;
            else user.GitHubId = externalId;
            db.Users.Add(user);
        }
        else
        {
            if (provider == "google") user.GoogleId = externalId;
            else user.GitHubId = externalId;
        }
        await db.SaveChangesAsync(ct);
        await HttpContext.SignOutAsync("External");

        var token = jwt.Create(user);
        var front = config["Frontend:Origin"] ?? "http://localhost:5173";
        return Redirect($"{front}/oauth?token={Uri.EscapeDataString(token)}&needsUserName={(string.IsNullOrWhiteSpace(user.UserName) ? "true" : "false")}");
    }

    private async Task<string> UniqueUserName(string raw, CancellationToken ct)
    {
        var baseName = new string((raw ?? "player").Where(char.IsLetterOrDigit).ToArray());
        if (baseName.Length < 2) baseName = "player";
        if (baseName.Length > 18) baseName = baseName[..18];
        var name = baseName;
        var i = 1;
        while (await db.Users.AnyAsync(u => u.UserName == name, ct))
            name = $"{baseName}{i++}";
        return name;
    }

    private AuthResponse ToAuth(User user) =>
        new(jwt.Create(user), user.Id, user.Email, user.UserName, string.IsNullOrWhiteSpace(user.UserName));
}
