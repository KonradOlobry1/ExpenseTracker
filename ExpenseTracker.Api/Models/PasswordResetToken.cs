namespace ExpenseTracker.Api.Models;

/// <summary>
/// A single-use credential that lets someone who has lost their password set a new one by
/// proving they can read the account's email.
/// </summary>
/// <remarks>
/// Deliberately the same shape as <see cref="RefreshToken"/>, and for the same reasons: the
/// hash rather than the raw token, so a row read out of the database is not a usable
/// credential; and an explicit spent marker, so a token that has already done its job is
/// rejected on a second attempt exactly like an expired one.
///
/// The lifetime is short where a refresh token's is long. A refresh token is only ever held by
/// a device that already authenticated; this one travels through email — a channel the account
/// owner does not fully control and which keeps its contents indefinitely — so the window in
/// which an old message is worth anything stays small.
/// </remarks>
public class PasswordResetToken
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Set when the token is spent — either by the reset it authorised, or by a later request
    /// that superseded it. Requesting a second link retires the first, so "send it again"
    /// cannot leave several live tokens, each of which would take the account.
    /// </summary>
    public DateTime? UsedAt { get; set; }

    public AppUser? User { get; set; }
}
