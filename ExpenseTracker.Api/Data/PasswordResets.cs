using System.Security.Cryptography;
using System.Text;
using ExpenseTracker.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data;

/// <summary>
/// Issuing and spending password reset tokens.
/// </summary>
/// <remarks>
/// Shared by the API controller, which devices call, and the web pages, which the emailed link
/// opens. Both have to agree on every rule here — the hash, the lifetime, retiring tokens the
/// account already had, cutting existing sessions, clearing lockout. Two copies would drift,
/// and the half that drifted would be whichever half had no test pointed at it.
///
/// Static over <see cref="ApiDbContext"/> rather than an injected service, matching
/// <see cref="DefaultCategories"/>: there is no state to hold and nothing to configure.
/// </remarks>
public static class PasswordResets
{
    /// <summary>
    /// An hour, against a refresh token's thirty days. This one sits in a mailbox rather than
    /// a device's secure storage: long enough to go and read the message, short enough that
    /// finding the mail months later is worth nothing.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Issues a fresh token, retiring any the account already held, and returns the raw value —
    /// the only moment it exists outside the caller's hands. Storage keeps the hash.
    /// </summary>
    public static async Task<string> IssueAsync(
        ApiDbContext db, string userId, CancellationToken ct = default)
    {
        await RetireOutstandingAsync(db, userId, ct);

        // 256 bits from the CSPRNG, hex-encoded. Opaque on purpose: nothing ever reads a claim
        // out of it, it is only ever looked up by hash.
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = userId,
            TokenHash = Hash(token),
            ExpiresAt = DateTime.UtcNow.Add(Lifetime),
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return token;
    }

    /// <summary>
    /// The token if it can still be spent, otherwise null. Unknown, already spent and expired
    /// are deliberately one answer: a caller holding a bad token learns nothing about which
    /// kind of bad it is.
    /// </summary>
    public static async Task<PasswordResetToken?> FindUsableAsync(
        ApiDbContext db, string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var stored = await db.PasswordResetTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null || stored.UsedAt is not null
            || stored.ExpiresAt <= DateTime.UtcNow || stored.User is null)
            return null;

        return stored;
    }

    /// <summary>
    /// Sets the new password and spends the token, or returns why the password was refused.
    /// </summary>
    /// <remarks>
    /// Identity's own token is generated and consumed in the same breath. Going through
    /// ResetPasswordAsync rather than writing the hash directly is what runs the configured
    /// password policy and rotates the security stamp; RemovePassword-then-AddPassword could
    /// leave an account with no password at all when the second half fails validation.
    ///
    /// A refused password does not spend the token — one typo must not cost the user their
    /// only link.
    /// </remarks>
    public static async Task<IdentityResult> CompleteAsync(
        ApiDbContext db, UserManager<AppUser> userManager,
        PasswordResetToken stored, string newPassword, CancellationToken ct = default)
    {
        var user = stored.User!;

        var identityToken = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, identityToken, newPassword);

        if (!result.Succeeded) return result;

        stored.UsedAt = DateTime.UtcNow;

        // Whoever knew the old password may well not be the account owner — that is the usual
        // reason to reset one. Cutting existing sessions means a thief holding a refresh token
        // loses access now rather than keeping it for up to thirty more days.
        await RevokeAllRefreshTokensAsync(db, user.Id, ct);

        // The user who locked themselves out guessing is exactly the user who then resets.
        // Leaving the lockout would make the new password look broken for fifteen minutes.
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);

        await db.SaveChangesAsync(ct);
        return result;
    }

    private static async Task RetireOutstandingAsync(
        ApiDbContext db, string userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var outstanding = await db.PasswordResetTokens
            .Where(t => t.UserId == userId && t.UsedAt == null)
            .ToListAsync(ct);

        foreach (var token in outstanding)
            token.UsedAt = now;
    }

    private static async Task RevokeAllRefreshTokensAsync(
        ApiDbContext db, string userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var live = await db.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in live)
            token.RevokedAt = now;
    }
}
