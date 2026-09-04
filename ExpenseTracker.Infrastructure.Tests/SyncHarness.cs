using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Contracts;
using ExpenseTracker.Domain.ValueObjects;
using ExpenseTracker.Infrastructure.External;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Presentation.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Tests;

/// <summary>
/// Hands out contexts over one connection, the way the app's registered factory does.
/// </summary>
/// <remarks>
/// The services under test take <see cref="IDbContextFactory{TContext}"/> rather than a
/// context, because sync runs several queries that must not share one change tracker.
/// </remarks>
public class TestDbContextFactory(DbContextOptions<AppDbContext> options)
    : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => new(options);
}

/// <summary>
/// Captures what the services logged.
/// </summary>
/// <remarks>
/// SyncService catches its own exceptions and returns false, which is right for the app and
/// unhelpful in a test: a failure reads only as "expected True, actual False". Failed
/// assertions can quote this instead.
/// </remarks>
public class CapturingLogger<T> : ILogger<T>
{
    public List<string> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
        Func<TState, Exception?, string> formatter)
        => Entries.Add($"{level}: {formatter(state, ex)}{(ex is null ? "" : " -> " + ex)}");

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}

/// <summary>An in-memory preference store, standing in for MAUI's Preferences.</summary>
public class FakePreferenceStore : IPreferenceStore
{
    private readonly Dictionary<string, object?> _values = [];

    public T Get<T>(string key, T defaultValue)
        => _values.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;

    public void Set<T>(string key, T value) => _values[key] = value;

    public void Remove(string key) => _values.Remove(key);

    public bool Has(string key) => _values.ContainsKey(key);
}

/// <summary>An in-memory token store, standing in for the OS keystore.</summary>
public class FakeSecureStore : ISecureStore
{
    private readonly Dictionary<string, string> _values = [];

    public Task<string?> GetAsync(string key)
        => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public void Remove(string key) => _values.Remove(key);
}

/// <summary>
/// Answers sync requests from a script, and records what was sent.
/// </summary>
public class StubApi : HttpMessageHandler
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public SyncPullResponse PullResponse { get; set; } =
        new(null, null, null, null, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), null);

    public HttpStatusCode PushStatus { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode PullStatus { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode LoginStatus { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode RegisterStatus { get; set; } = HttpStatusCode.OK;
    public HttpStatusCode RefreshStatus { get; set; } = HttpStatusCode.OK;

    /// <summary>Every request throws instead of getting a response — a DNS failure, a
    /// connection refused, anything transport-level rather than an HTTP status.</summary>
    public bool ThrowOnSend { get; set; }

    /// <summary>Every push payload the client sent, in order.</summary>
    public List<SyncPushRequest> Pushes { get; } = [];

    /// <summary>Every pull URL the client requested, in order.</summary>
    public List<string> PullUrls { get; } = [];

    public int RefreshCallCount { get; private set; }

    /// <summary>Every refresh token a revoke request was made with, in order.</summary>
    public List<string> RevokedTokens { get; } = [];

    private int _tokenCounter;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ThrowOnSend)
            throw new HttpRequestException("Stubbed network failure.");

        var url = request.RequestUri!.ToString();

        if (url.Contains("/api/auth/login"))
            return AuthResponseMessage(LoginStatus);

        if (url.Contains("/api/auth/register"))
            return AuthResponseMessage(RegisterStatus);

        if (url.Contains("/api/auth/refresh"))
        {
            RefreshCallCount++;
            return AuthResponseMessage(RefreshStatus);
        }

        if (url.Contains("/api/auth/revoke"))
        {
            var body = await request.Content!.ReadFromJsonAsync<RevokeRequest>(Json, cancellationToken);
            RevokedTokens.Add(body!.RefreshToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (url.Contains("/api/sync/push"))
        {
            var body = await request.Content!.ReadFromJsonAsync<SyncPushRequest>(Json, cancellationToken);
            Pushes.Add(body!);
            return new HttpResponseMessage(PushStatus);
        }

        if (url.Contains("/api/sync/pull"))
        {
            PullUrls.Add(url);
            return PullStatus == HttpStatusCode.OK
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(PullResponse) }
                : new HttpResponseMessage(PullStatus);
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    /// <summary>A fresh pair each call — <c>_tokenCounter</c> distinguishes them, so a test
    /// can confirm a refresh actually replaced the stored value rather than merely re-storing
    /// the same one.</summary>
    private HttpResponseMessage AuthResponseMessage(HttpStatusCode status)
    {
        if (status != HttpStatusCode.OK) return new HttpResponseMessage(status);

        _tokenCounter++;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new
            {
                Token = $"stub-token-{_tokenCounter}",
                Expiry = DateTime.UtcNow.AddHours(24),
                RefreshToken = $"stub-refresh-{_tokenCounter}"
            })
        };
    }

    private record RevokeRequest(string RefreshToken);
}

/// <summary>
/// A signed-in client with an in-memory replica, a fake preference store and a stubbed API.
/// </summary>
/// <remarks>
/// This is what the abstraction of MAUI's Preferences and SecureStorage bought: SyncService
/// is now ordinary code that can be driven from a test, so conflict resolution, tombstone
/// handling and the settings merge are covered directly rather than only through the server.
/// </remarks>
public sealed class SyncHarness : IDisposable
{
    private readonly SqliteConnection _connection;

    public FakePreferenceStore Prefs { get; } = new();
    public FakeSecureStore Secrets { get; } = new();
    public StubApi Api { get; } = new();
    public LocalSettings Settings { get; }
    public CurrencyService Currency { get; }
    public LocalizationService Localization { get; }
    public ThemeService Theme { get; }
    public SyncService Sync { get; }
    public AuthService Auth { get; }
    public CapturingLogger<SyncService> SyncLog { get; } = new();
    public CapturingLogger<AuthService> AuthLog { get; } = new();
    public IDbContextFactory<AppDbContext> DbFactory { get; }

    public SyncHarness(bool signedIn = true)
    {
        _connection = new SqliteConnection($"Data Source=sync-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        DbFactory = new TestDbContextFactory(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

        using (var db = DbFactory.CreateDbContext())
            db.Database.EnsureCreated();

        Settings = new LocalSettings(Prefs);
        Currency = new CurrencyService(Prefs, Settings);
        Localization = new LocalizationService(Prefs, Settings);
        Theme = new ThemeService(Prefs, Settings);

        Auth = new AuthService(
            new HttpClient(Api), AuthLog, DbFactory, Prefs, Secrets);

        Sync = new SyncService(
            Auth, DbFactory, new HttpClient(Api), SyncLog,
            Currency, Localization, Theme, Prefs, Settings);

        if (signedIn) SignIn();
    }

    /// <summary>
    /// Puts the harness in the state a successful login leaves behind: a token in secure
    /// storage and an expiry in preferences. Written directly rather than through LoginAsync
    /// so a test about sync does not depend on the login endpoint too.
    /// </summary>
    /// <summary>
    /// Passing <paramref name="refreshToken"/> as null simulates a device that predates
    /// refresh tokens, or one that already had its refresh token cleared after the server
    /// rejected it outright (as opposed to merely being unreachable).
    /// </summary>
    public void SignIn(DateTime? expiry = null, string? refreshToken = "stub-refresh-token")
    {
        // ApiBaseUrl is a fixed constant now, not per-device state, so signing in only means
        // storing the tokens and expiry — StubApi answers any host, so the constant's actual
        // value is irrelevant here.
        Secrets.SetAsync("jwt_token", "stub-token").GetAwaiter().GetResult();
        Prefs.Set("jwt_expiry", (expiry ?? DateTime.UtcNow.AddHours(24)).Ticks);
        if (refreshToken is not null)
            Secrets.SetAsync("jwt_refresh_token", refreshToken).GetAwaiter().GetResult();
    }

    public AppDbContext NewDbContext() => DbFactory.CreateDbContext();

    /// <summary>The settings this device would push right now.</summary>
    public SyncSettingsDto CurrentSettings() =>
        new(Currency.Selected.Code, Localization.CurrentLanguage, Theme.IsDarkMode, Settings.UpdatedAt);

    // The default name deliberately does not collide with a built-in: only the test that is
    // about name collisions should be exercising that path.
    public static SyncCategoryDto Category(
        Guid syncId, string name = "Imported", bool isDeleted = false, DateTime? updatedAt = null)
        => new(syncId, name, "restaurant", "#F44336", true,
               new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), updatedAt, isDeleted);

    public static SyncExpenseDto Expense(
        Guid syncId, Guid categorySyncId, decimal amount = 10m, string description = "Test",
        bool isDeleted = false, DateTime? updatedAt = null)
        => new(syncId, description, amount, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
               categorySyncId, null, new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
               updatedAt, isDeleted);

    public static SyncSettingsDto SettingsDto(
        string currency = "PLN", string language = "pl", bool dark = true, DateTime? updatedAt = null)
        => new(currency, language, dark,
               updatedAt ?? new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

    /// <summary>Runs a sync and fails the test with the reason and log if it did not succeed.</summary>
    public async Task<SyncResult> SyncOrExplainAsync()
    {
        var result = await Sync.SyncAsync();
        if (!result.Succeeded)
            Assert.Fail($"Sync failed ({result.Failure}): " + string.Join(" | ", SyncLog.Entries));
        return result;
    }

    public void Dispose() => _connection.Dispose();
}
