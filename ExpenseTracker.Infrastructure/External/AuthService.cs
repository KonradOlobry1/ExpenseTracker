using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Presentation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.External;

public class AuthService : IAuthService
{
    private const string TokenKey = "jwt_token";
    private const string ExpiryKey = "jwt_expiry";
    private const string RefreshTokenKey = "jwt_refresh_token";

    private readonly HttpClient _http;
    private readonly ILogger<AuthService> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IPreferenceStore _prefs;
    private readonly ISecureStore _secrets;

    public AuthService(
        HttpClient http,
        ILogger<AuthService> logger,
        IDbContextFactory<AppDbContext> dbFactory,
        IPreferenceStore prefs,
        ISecureStore secrets)
    {
        _http = http;
        _logger = logger;
        _dbFactory = dbFactory;
        _prefs = prefs;
        _secrets = secrets;
    }

    // Fixed, not a preference. It used to be user-editable and read from Preferences with
    // this as the fallback; nothing in the app ever needed a different value on a real device,
    // and a stale one from development had no way to be fixed short of clearing app storage.
    // Point a build at another server by changing this constant.
    public const string ApiBaseUrl =
        "https://expensetracker-e7bgaqgsbjhwarau.polandcentral-01.azurewebsites.net";

    string IAuthService.ApiBaseUrl => ApiBaseUrl;

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            var baseUrl = ApiBaseUrl.TrimEnd('/');
            var response = await _http.PostAsJsonAsync(
                $"{baseUrl}/api/auth/login", new { Email = email, Password = password }, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Login rejected by server: {StatusCode}.", response.StatusCode);
                return AuthResult.Fail(ReasonForStatus(response.StatusCode));
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct);
            if (auth is null)
            {
                _logger.LogError("Login succeeded but the server returned an empty body.");
                return AuthResult.Fail(AuthFailureReason.ServerError);
            }

            await StoreTokensAsync(auth);
            return AuthResult.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Login failed while contacting the API.");
            return AuthResult.Fail(AuthFailureReason.NetworkError);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Login response could not be parsed.");
            return AuthResult.Fail(AuthFailureReason.ServerError);
        }
    }

    public async Task<AuthResult> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            var baseUrl = ApiBaseUrl.TrimEnd('/');
            var response = await _http.PostAsJsonAsync(
                $"{baseUrl}/api/auth/register", new { Email = email, Password = password }, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Registration rejected by server: {StatusCode} {Body}.",
                    response.StatusCode, body);
                // Validation failures (weak password, email taken) arrive as 400 alongside
                // everything else the server can reject a request for; there is no reason
                // enum granular enough to tell those apart, so both read as "server error".
                return AuthResult.Fail(ReasonForStatus(response.StatusCode));
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct);
            if (auth is null)
            {
                _logger.LogError("Registration succeeded but the server returned an empty body.");
                return AuthResult.Fail(AuthFailureReason.ServerError);
            }

            await StoreTokensAsync(auth);
            return AuthResult.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(ex, "Registration failed while contacting the API.");
            return AuthResult.Fail(AuthFailureReason.NetworkError);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Registration response could not be parsed.");
            return AuthResult.Fail(AuthFailureReason.ServerError);
        }
    }

    /// <summary>
    /// 401 and 423 are the only statuses <c>AuthController</c> returns for a reason the user
    /// can act on (wrong password, too many attempts); everything else — a 500, a timeout
    /// that still produced a response, a validation 400 on register — is bucketed together
    /// since none of them are the user's fault and the app can't tell them apart usefully.
    /// </summary>
    private static AuthFailureReason ReasonForStatus(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => AuthFailureReason.InvalidCredentials,
        HttpStatusCode.Locked => AuthFailureReason.AccountLocked,
        _ => AuthFailureReason.ServerError,
    };

    /// <summary>
    /// Signs out and discards everything belonging to that account on this device.
    /// </summary>
    /// <remarks>
    /// Clearing only the token used to leave the local replica and the last-sync marker in
    /// place, so signing in as somebody else kept the previous account's rows on screen — and
    /// worse, the next edit to one of them pushed it into the new account. The replica is a
    /// cache of one account's data; it has no meaning once that account is signed out.
    ///
    /// Also revokes the refresh token server-side, best-effort, before clearing it locally.
    /// Without this, "signing out" only ever meant forgetting the token on this device — a
    /// copy captured earlier (a stolen device, a compromised backup) would still work, since
    /// nothing had told the server the session was over.
    ///
    /// The API base URL is not touched — it is a fixed constant now, not per-device state.
    /// </remarks>
    public async Task LogoutAsync()
    {
        var refreshToken = await _secrets.GetAsync(RefreshTokenKey);
        if (!string.IsNullOrEmpty(refreshToken))
        {
            try
            {
                var baseUrl = ApiBaseUrl.TrimEnd('/');
                await _http.PostAsJsonAsync($"{baseUrl}/api/auth/revoke", new { RefreshToken = refreshToken });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // Signing out locally must succeed even when the server can't be reached —
                // an unreachable revoke just means a stale token lingers server-side until it
                // expires on its own; it does not block the user from leaving the app.
                _logger.LogWarning(ex, "Could not revoke the refresh token on the server.");
            }
        }

        _secrets.Remove(TokenKey);
        _secrets.Remove(RefreshTokenKey);
        _prefs.Remove(ExpiryKey);
        _prefs.Remove(SyncService.LastSyncKey);
        _prefs.Remove(LocalSettings.StampKey);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();

            // Expenses and subscriptions reference categories, so they go first. Categories
            // themselves are restored by the seed data in the migrations on next launch, and
            // re-synced from the account after the next sign-in.
            await db.Expenses.IgnoreQueryFilters().ExecuteDeleteAsync();
            await db.Subscriptions.IgnoreQueryFilters().ExecuteDeleteAsync();
            await db.Incomes.IgnoreQueryFilters().ExecuteDeleteAsync();
            await db.Categories.IgnoreQueryFilters().Where(c => !c.IsSystem).ExecuteDeleteAsync();
        }
        catch (DbException ex)
        {
            // Signing out must succeed regardless: a stale replica is recoverable, a session
            // that cannot be ended is not.
            _logger.LogError(ex, "Could not clear the local database on sign-out.");
        }
    }

    public async Task<bool> IsLoggedInAsync()
    {
        try
        {
            var token = await _secrets.GetAsync(TokenKey);
            if (string.IsNullOrEmpty(token)) return false;

            var expiryTicks = _prefs.Get<long>(ExpiryKey, 0);
            var expiry = expiryTicks == 0 ? DateTime.MinValue : new DateTime(expiryTicks, DateTimeKind.Utc);

            if (expiry > DateTime.UtcNow) return true;

            // The access token has run out. Rather than every caller of IsLoggedInAsync
            // sending the user back to a password prompt the moment a day has passed, try the
            // refresh token first — this is what lets a device stay signed in indefinitely as
            // long as it syncs at least once within the refresh token's 30-day lifetime,
            // instead of once every 24 hours. Every existing call site (MainLayout, Settings,
            // SyncService) already goes through this method, so they all gain this for free.
            return await TryRefreshAsync();
        }
        catch (Exception ex)
        {
            // The keystore is unavailable on some devices, and throws rather than returning null.
            _logger.LogError(ex, "Could not read the stored token; treating the user as signed out.");
            return false;
        }
    }

    private async Task<bool> TryRefreshAsync()
    {
        var refreshToken = await _secrets.GetAsync(RefreshTokenKey);
        if (string.IsNullOrEmpty(refreshToken)) return false;

        try
        {
            var baseUrl = ApiBaseUrl.TrimEnd('/');
            var response = await _http.PostAsJsonAsync(
                $"{baseUrl}/api/auth/refresh", new { RefreshToken = refreshToken });

            if (!response.IsSuccessStatusCode)
            {
                // The server rejected this specific token — expired, already used, or
                // revoked. Keeping it would just mean trying the same rejected token again
                // next time, so it goes; the user needs a password to get a new one.
                _logger.LogWarning("Refresh rejected by server: {StatusCode}.", response.StatusCode);
                _secrets.Remove(RefreshTokenKey);
                return false;
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
            if (auth is null) return false;

            await StoreTokensAsync(auth);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Unreachable, or responded with something unparseable — neither is evidence the
            // refresh token itself is bad, so unlike a rejection it stays for next time.
            _logger.LogWarning(ex, "Could not refresh the session; will retry next time.");
            return false;
        }
    }

    private async Task StoreTokensAsync(AuthResponse auth)
    {
        await _secrets.SetAsync(TokenKey, auth.Token);
        _prefs.Set(ExpiryKey, auth.Expiry.Ticks);
        await _secrets.SetAsync(RefreshTokenKey, auth.RefreshToken);
    }

    public async Task<bool> HasStoredSessionAsync()
    {
        // No expiry check on purpose — see IAuthService. A stored token means this device
        // belongs to an account, which is what the app gate asks about.
        var token = await _secrets.GetAsync(TokenKey);
        return !string.IsNullOrEmpty(token);
    }

    public async Task<UserInfo?> GetCurrentUserAsync()
    {
        try
        {
            var token = await _secrets.GetAsync(TokenKey);
            if (string.IsNullOrEmpty(token)) return null;

            var parts = token.Split('.');
            if (parts.Length < 2) return null;

            var payload = parts[1];
            var padded = (payload.Length % 4) switch
            {
                2 => payload + "==",
                3 => payload + "=",
                _ => payload
            };
            padded = padded.Replace('-', '+').Replace('_', '/');

            var bytes = Convert.FromBase64String(padded);
            var json = Encoding.UTF8.GetString(bytes);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() ?? "" : "";
            var userId = root.TryGetProperty("sub", out var subProp) ? subProp.GetString() ?? "" : "";

            return new UserInfo(email, userId);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            _logger.LogError(ex, "Stored JWT could not be decoded.");
            return null;
        }
    }

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            return await _secrets.GetAsync(TokenKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the stored token from secure storage.");
            return null;
        }
    }

    private record AuthResponse(string Token, DateTime Expiry, string RefreshToken);
}
