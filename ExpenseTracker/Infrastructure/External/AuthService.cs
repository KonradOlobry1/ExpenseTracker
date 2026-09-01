using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.External;

public class AuthService : IAuthService
{
    private const string TokenKey = "jwt_token";
    private const string ExpiryKey = "jwt_expiry";
    private const string ApiUrlKey = "api_base_url";

    private readonly HttpClient _http;
    private readonly ILogger<AuthService> _logger;

    public AuthService(HttpClient http, ILogger<AuthService> logger)
    {
        _http = http;
        _logger = logger;
    }

    // A fresh install has no saved preference, and an empty base URL aborts login before the
    // request leaves the device — which surfaced as "invalid credentials". Defaulting to the
    // hosted service means the field arrives filled in; self-hosters overwrite it on the
    // login page and their value is what gets persisted.
    public const string DefaultApiBaseUrl =
        "https://expensetracker-e7bgaqgsbjhwarau.polandcentral-01.azurewebsites.net";

    public string? ApiBaseUrl
    {
        get => Preferences.Default.Get<string?>(ApiUrlKey, DefaultApiBaseUrl);
        set
        {
            if (value is not null)
                Preferences.Default.Set(ApiUrlKey, value);
            else
                Preferences.Default.Remove(ApiUrlKey);
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

            await SecureStorage.Default.SetAsync(TokenKey, auth.Token);
            Preferences.Default.Set(ExpiryKey, auth.Expiry.Ticks);
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

            await SecureStorage.Default.SetAsync(TokenKey, auth.Token);
            Preferences.Default.Set(ExpiryKey, auth.Expiry.Ticks);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogError(ex, "Registration failed while contacting the API.");
            return false;
        }
    }

    public Task LogoutAsync()
    {
        SecureStorage.Default.Remove(TokenKey);
        Preferences.Default.Remove(ExpiryKey);
        return Task.CompletedTask;
    }

    public async Task<bool> IsLoggedInAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(TokenKey);
            if (string.IsNullOrEmpty(token)) return false;

            var expiryTicks = Preferences.Default.Get<long>(ExpiryKey, 0);
            if (expiryTicks == 0) return false;

            var expiry = new DateTime(expiryTicks, DateTimeKind.Utc);
            return expiry > DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            // SecureStorage throws on some devices when the keystore is unavailable.
            _logger.LogError(ex, "Could not read the stored token; treating the user as signed out.");
            return false;
        }
    }

    public async Task<UserInfo?> GetCurrentUserAsync()
    {
        try
        {
            var token = await SecureStorage.Default.GetAsync(TokenKey);
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
            return await SecureStorage.Default.GetAsync(TokenKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not read the stored token from SecureStorage.");
            return null;
        }
    }

    private record AuthResponse(string Token, DateTime Expiry);
}
