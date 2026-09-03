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

    /// <summary>Every push payload the client sent, in order.</summary>
    public List<SyncPushRequest> Pushes { get; } = [];

    /// <summary>Every pull URL the client requested, in order.</summary>
    public List<string> PullUrls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!.ToString();

        if (url.Contains("/api/sync/push"))
        {
            var body = await request.Content!.ReadFromJsonAsync<SyncPushRequest>(Json, cancellationToken);
            Pushes.Add(body!);
            return new HttpResponseMessage(PushStatus);
        }

        if (url.Contains("/api/sync/pull"))
        {
            PullUrls.Add(url);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(PullResponse)
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
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
    public void SignIn(DateTime? expiry = null)
    {
        Secrets.SetAsync("jwt_token", "stub-token").GetAwaiter().GetResult();
        Prefs.Set("jwt_expiry", (expiry ?? DateTime.UtcNow.AddHours(24)).Ticks);
        Auth.ApiBaseUrl = "https://stub.local";
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

    /// <summary>Runs a sync and fails the test with the logged reason if it did not succeed.</summary>
    public async Task SyncOrExplainAsync()
    {
        if (!await Sync.SyncAsync())
            Assert.Fail("Sync failed: " + string.Join(" | ", SyncLog.Entries));
    }

    public void Dispose() => _connection.Dispose();
}
