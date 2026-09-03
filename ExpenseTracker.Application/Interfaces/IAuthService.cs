namespace ExpenseTracker.Application.Interfaces;

public record UserInfo(string Email, string UserId);

public interface IAuthService
{
    Task<bool> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<bool> RegisterAsync(string email, string password, CancellationToken ct = default);
    Task LogoutAsync();

    /// <summary>Whether there is a valid, unexpired session. Sync requires this.</summary>
    Task<bool> IsLoggedInAsync();

    /// <summary>
    /// Whether this device has been signed in to an account at all, expired or not.
    /// </summary>
    /// <remarks>
    /// Deliberately weaker than <see cref="IsLoggedInAsync"/>, and the two must not be
    /// confused. The app is offline-first: its local replica is useful with no network, and
    /// the access token lasts a day with no refresh. Gating the whole app on an unexpired
    /// token would lock a user out of their own expense history on a train with no signal.
    ///
    /// So this gates access to the app, and <see cref="IsLoggedInAsync"/> gates sync. A user
    /// whose token has expired keeps working locally and is asked to sign in again the next
    /// time they sync.
    /// </remarks>
    Task<bool> HasStoredSessionAsync();
    Task<UserInfo?> GetCurrentUserAsync();
    Task<string?> GetTokenAsync();
    string? ApiBaseUrl { get; set; }
}
