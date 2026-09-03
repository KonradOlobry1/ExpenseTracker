namespace ExpenseTracker.Api.Models;

/// <summary>
/// A long-lived credential that renews an access token without re-entering a password.
/// </summary>
/// <remarks>
/// One user can hold several of these at once — a phone, a desktop and a browser are three
/// simultaneous sessions today, and revoking the other two on a fresh login would silently
/// break sync on whichever device didn't just sign in. There is no unique constraint tying a
/// user to a single row.
///
/// <see cref="TokenHash"/>, never the raw token: a row read out of the database (a backup, a
/// compromised connection string) must not be a usable credential, the same reasoning as a
/// password hash. The raw value only ever exists in the response sent to the client and in
/// the client's own secure storage.
/// </remarks>
public class RefreshToken
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Set the moment this token is used to refresh (rotation) or the device signs out. A
    /// revoked token that is presented again — the replay a stolen, already-used token would
    /// attempt — is rejected exactly like an expired one.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    public AppUser? User { get; set; }
}
