using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.DTOs;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseTracker.Api.Controllers;

[ApiController]
[Route("api/auth")]
// Lockout protects a single account from guessing; this protects against spraying many
// accounts from one source. Refresh and Revoke sit under the same policy: a refresh token
// is itself the credential on those two, unauthenticated the way a password is.
[EnableRateLimiting("auth")]
public class AuthController(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    ApiDbContext db,
    IConfiguration configuration,
    IPasswordResetSender resetSender) : ControllerBase
{
    // Sliding via rotation: every refresh issues a fresh 30-day token, so a device in active
    // use never has to fall back to a password, while one that stops syncing eventually needs
    // one again.
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(
        [FromBody] RegisterRequest request, CancellationToken ct)
    {
        var user = new AppUser { UserName = request.Email, Email = request.Email };

        // UserManager and SignInManager take no CancellationToken — they read their own from
        // a protected property that is not settable from here. Identity's own calls therefore
        // run to completion regardless; ct reaches everything after them.
        var result = await userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        // Matches the web registration path (Web/Account/Login.razor): an account with no
        // categories cannot create an expense at all.
        await DefaultCategories.EnsureForUserAsync(db, user.Id, ct);

        return Ok(await IssueTokensAsync(user, ct));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
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

        return Ok(await IssueTokensAsync(user, ct));
    }

    /// <summary>
    /// Exchanges an unexpired, unrevoked refresh token for a new access token and a new
    /// refresh token — rotation, not renewal in place. The old token stops working the moment
    /// this succeeds, so a copy that leaked earlier and was already used by its rightful owner
    /// fails on its next attempt exactly like a stolen one would; this is the cheap version of
    /// theft detection, not a substitute for one.
    /// </summary>
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh(
        [FromBody] RefreshRequest request, CancellationToken ct)
    {
        var hash = Hash(request.RefreshToken);
        var stored = await db.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.TokenHash == hash, ct);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= DateTime.UtcNow
            || stored.User is null)
            return Unauthorized("Invalid or expired refresh token.");

        stored.RevokedAt = DateTime.UtcNow;

        // Revoking the old token and issuing the new pair share one SaveChanges inside
        // IssueTokensAsync, so rotation stays atomic: a cancellation between the two would
        // otherwise be able to retire a token without handing back its replacement.
        return Ok(await IssueTokensAsync(stored.User, ct));
    }

    /// <summary>
    /// Ends one session server-side. The device calls this on sign-out, before clearing its
    /// own copy — otherwise "signing out" only ever meant forgetting the token locally, and a
    /// copy of it captured earlier would still work.
    /// </summary>
    /// <remarks>
    /// Always 200: whether the token existed, was already revoked, or never did, the caller's
    /// goal — this token must not work anymore — is satisfied either way, and there's nothing
    /// useful to tell an anonymous caller about which case it was.
    /// </remarks>
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RefreshRequest request, CancellationToken ct)
    {
        var hash = Hash(request.RefreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash, ct);

        if (stored is not null && stored.RevokedAt is null)
        {
            stored.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Ok();
    }

    /// <summary>
    /// Starts a password reset: issues a single-use token and hands it to the sender, which
    /// delivers it to the address on the account.
    /// </summary>
    /// <remarks>
    /// Always 200, saying nothing about whether the address is registered — the same reasoning
    /// as Login treating an unknown email and a wrong password identically. An endpoint that
    /// answered 404 here would be a membership oracle needing no credentials at all.
    ///
    /// The response time still differs slightly, because only a real account does database
    /// work. Closing that would mean doing equivalent work for addresses that do not exist;
    /// against an endpoint that is already rate limited per IP, the timing signal is not worth
    /// the machinery, but it is a real remaining difference rather than none.
    /// </remarks>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request, CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(request.Email);

        if (user is not null)
        {
            // Issuing retires any link the account already had, so "send it again" cannot
            // leave several live tokens.
            var token = await PasswordResets.IssueAsync(db, user.Id, ct);

            // Only the sender ever sees the raw token. It is never returned from here.
            await resetSender.SendAsync(request.Email, token, ct);
        }

        return Ok();
    }

    /// <summary>
    /// Spends a reset token and sets the new password.
    /// </summary>
    /// <remarks>
    /// Two separate checks, deliberately. The opaque token establishes who is asking — it is
    /// the proof that this caller can read the account's email. What
    /// <see cref="PasswordResets.CompleteAsync"/> does with Identity's own token is what
    /// enforces the password policy. The rules live there because the web pages spend tokens
    /// too, and both routes have to behave identically.
    /// </remarks>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request, CancellationToken ct)
    {
        var stored = await PasswordResets.FindUsableAsync(db, request.Token, ct);

        if (stored is null)
            return BadRequest("This reset link is invalid or has expired.");

        var result = await PasswordResets.CompleteAsync(db, userManager, stored, request.NewPassword, ct);

        return result.Succeeded ? Ok() : BadRequest(result.Errors);
    }

    private async Task<AuthResponse> IssueTokensAsync(AppUser user, CancellationToken ct)
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
        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        var refreshToken = GenerateOpaqueToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Hash(refreshToken),
            ExpiresAt = DateTime.UtcNow.Add(RefreshTokenLifetime),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return new AuthResponse(accessToken, expiry, refreshToken);
    }

    /// <summary>256 bits from the CSPRNG, hex-encoded — opaque on purpose. Unlike the JWT
    /// access token, nothing ever needs to read a claim out of a refresh token; it is only
    /// ever looked up by its hash.</summary>
    private static string GenerateOpaqueToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
