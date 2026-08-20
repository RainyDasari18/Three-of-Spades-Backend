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
public class AuthController(AppDbContext db, JwtTokenService jwt) : ControllerBase
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

    private AuthResponse ToAuth(User user) =>
        new(jwt.Create(user), user.Id, user.Email, user.UserName, string.IsNullOrWhiteSpace(user.UserName));
}
