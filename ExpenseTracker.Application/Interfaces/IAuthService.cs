namespace ExpenseTracker.Application.Interfaces;

public record UserInfo(string Email, string UserId);

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<AuthResult> RegisterAsync(string email, string password, CancellationToken ct = default);
    Task LogoutAsync();

    /// <summary>
    /// Whether the caller can act as this account right now — a bearer token that has not
    /// expired, or one silently renewed via the refresh token if it had. Sync requires this.
    /// </summary>
    Task<bool> IsLoggedInAsync();

    /// <summary>
    /// Whether this device has been signed in to an account at all, independent of whether
    /// either token is still good.
    /// </summary>
    /// <remarks>
    /// Deliberately weaker than <see cref="IsLoggedInAsync"/>, and the two must not be
    /// confused. The app is offline-first: its local replica is useful with no network, and
    /// gating the whole app on <see cref="IsLoggedInAsync"/> would mean a device with no
    /// signal — where a network call can't even be attempted — locks the user out of their
    /// own expense history rather than showing them what's already on the device.
    ///
    /// So this gates access to the app, and <see cref="IsLoggedInAsync"/> gates sync. A device
    /// this returns true for keeps working locally even when <see cref="IsLoggedInAsync"/>
    /// currently answers false because there is no network to attempt a refresh over; the
    /// user is only asked to sign in again once a sync actually fails.
    /// </remarks>
    Task<bool> HasStoredSessionAsync();
    Task<UserInfo?> GetCurrentUserAsync();
    Task<string?> GetTokenAsync();

    /// <summary>
    /// The API this client talks to. Fixed per build — there is exactly one production
    /// server, and letting it vary at run time only ever meant a stale value from
    /// development surviving into a signed-in device with no way for the user to fix it.
    /// </summary>
    string ApiBaseUrl { get; }
}
