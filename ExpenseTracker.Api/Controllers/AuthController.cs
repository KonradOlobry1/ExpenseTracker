using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/auth")]
// Lockout protects a single account from guessing; this protects against spraying many
// accounts from one source.
[EnableRateLimiting("auth")]
public class AuthController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    ApiDbContext db,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        var user = new AppUser { UserName = request.Email, Email = request.Email };
        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        // Matches the web registration path (Web/Account/Login.razor): an account with no
        // categories cannot create an expense at all.
        await DefaultCategories.EnsureForUserAsync(db, user.Id);

        return Ok(GenerateToken(user));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        // CheckPasswordSignInAsync, not UserManager.CheckPasswordAsync: the latter does not
        // record failed attempts, so lockout could never trigger on this path.
        // The response for an unknown email is identical to a wrong password, so the
        // endpoint does not reveal which addresses are registered.
        if (user is null)
            return Unauthorized("Invalid email or password.");

        var signIn = await signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (signIn.IsLockedOut)
            return StatusCode(StatusCodes.Status423Locked,
                "Too many failed attempts. Try again in a few minutes.");

        if (!signIn.Succeeded)
            return Unauthorized("Invalid email or password.");

        return Ok(GenerateToken(user));
    }

    private AuthResponse GenerateToken(AppUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key not configured")));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddHours(24);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            issuer: configuration["Jwt:Issuer"],
            audience: configuration["Jwt:Audience"],
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new AuthResponse(new JwtSecurityTokenHandler().WriteToken(token), expiry);
    }
}
