using System.Data.Common;
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
    private const string ApiUrlKey = "api_base_url";

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

    // A fresh install has no saved preference, and an empty base URL aborts login before the
    // request leaves the device — which surfaced as "invalid credentials". Defaulting to the
    // hosted service means the field arrives filled in; self-hosters overwrite it on the
    // login page and their value is what gets persisted.
    public const string DefaultApiBaseUrl =
        "https://expensetracker-e7bgaqgsbjhwarau.polandcentral-01.azurewebsites.net";

    public string? ApiBaseUrl
    {
        get => _prefs.Get<string?>(ApiUrlKey, DefaultApiBaseUrl);
        set
        {
            if (value is not null)
                _prefs.Set(ApiUrlKey, value);
            else
                _prefs.Remove(ApiUrlKey);
        }
    }

    public async Task<bool> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            var baseUrl = ApiBaseUrl?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogWarning("Login aborted: no API base URL configured.");
                return false;
            }

            var response = await _http.PostAsJsonAsync(
                $"{baseUrl}/api/auth/login", new { Email = email, Password = password }, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Login rejected by server: {StatusCode}.", response.StatusCode);
                return false;
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct);
            if (auth is null)
            {
                _logger.LogError("Login succeeded but the server returned an empty body.");
                return false;
            }

            await _secrets.SetAsync(TokenKey, auth.Token);
            _prefs.Set(ExpiryKey, auth.Expiry.Ticks);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Login failed while contacting the API.");
            return false;
        }
    }

    public async Task<bool> RegisterAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            var baseUrl = ApiBaseUrl?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogWarning("Registration aborted: no API base URL configured.");
                return false;
            }

            var response = await _http.PostAsJsonAsync(
                $"{baseUrl}/api/auth/register", new { Email = email, Password = password }, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Registration rejected by server: {StatusCode} {Body}.",
                    response.StatusCode, body);
                return false;
            }

            var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(cancellationToken: ct);
            if (auth is null)
            {
                _logger.LogError("Registration succeeded but the server returned an empty body.");
                return false;
            }

            await _secrets.SetAsync(TokenKey, auth.Token);
            _prefs.Set(ExpiryKey, auth.Expiry.Ticks);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Registration failed while contacting the API.");
            return false;
        }
    }

    /// <summary>
    /// Signs out and discards everything belonging to that account on this device.
    /// </summary>
    /// <remarks>
    /// Clearing only the token used to leave the local replica and the last-sync marker in
    /// place, so signing in as somebody else kept the previous account's rows on screen — and
    /// worse, the next edit to one of them pushed it into the new account. The replica is a
    /// cache of one account's data; it has no meaning once that account is signed out.
    ///
    /// The API base URL deliberately survives: it describes the server, not the account, and
    /// clearing it would make the next sign-in fail with an empty Server URL field.
    /// </remarks>
    public async Task LogoutAsync()
    {
        _secrets.Remove(TokenKey);
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
            if (expiryTicks == 0) return false;

            var expiry = new DateTime(expiryTicks, DateTimeKind.Utc);
            return expiry > DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // The keystore is unavailable on some devices, and throws rather than returning null.
            _logger.LogError(ex, "Could not read the stored token; treating the user as signed out.");
            return false;
        }
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

    private record AuthResponse(string Token, DateTime Expiry);
}
