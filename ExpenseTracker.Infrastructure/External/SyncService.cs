using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Contracts;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Infrastructure.Persistence;
using ExpenseTracker.Presentation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.External;

public class SyncService : ISyncService
{
    // Public so sign-out can clear it: a marker from one account must not carry into the next.
    public const string LastSyncKey = "last_sync_time";

    private readonly IAuthService _auth;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly HttpClient _http;
    private readonly ILogger<SyncService> _logger;
    private readonly ICurrencyService _currency;
    private readonly ILocalizationService _localization;
    private readonly IThemeService _theme;
    private readonly IPreferenceStore _prefs;
    private readonly LocalSettings _settings;

    public SyncService(
        IAuthService auth,
        IDbContextFactory<AppDbContext> dbFactory,
        HttpClient http,
        ILogger<SyncService> logger,
        ICurrencyService currency,
        ILocalizationService localization,
        IThemeService theme,
        IPreferenceStore prefs,
        LocalSettings settings)
    {
        _prefs = prefs;
        _settings = settings;
        _auth = auth;
        _dbFactory = dbFactory;
        _http = http;
        _logger = logger;
        _currency = currency;
        _localization = localization;
        _theme = theme;
    }

    public DateTime? LastSyncTime
    {
        get
        {
            var ticks = _prefs.Get<long>(LastSyncKey, 0);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
        private set
        {
            if (value.HasValue)
                _prefs.Set(LastSyncKey, value.Value.Ticks);
            else
                _prefs.Remove(LastSyncKey);
        }
    }

    public bool IsSyncing { get; private set; }

    /// <summary>This is the device client — syncing is the entire point of it.</summary>
    public bool IsSupported => true;

    public event Action? SyncStateChanged;

    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        if (!await _auth.IsLoggedInAsync())
        {
            // Distinguishes "never signed in" from "was signed in, needs attention" — the
            // second is what sends the user back to the login page; the first never should,
            // since sync simply has nothing to do yet.
            var reason = await _auth.HasStoredSessionAsync()
                ? SyncFailureReason.SessionExpired
                : SyncFailureReason.NotSignedIn;
            return SyncResult.Fail(reason);
        }

        IsSyncing = true;
        SyncStateChanged?.Invoke();

        try
        {
            var token = await _auth.GetTokenAsync();
            if (token is null)
            {
                _logger.LogWarning("Sync aborted: no stored auth token.");
                return SyncResult.Fail(SyncFailureReason.SessionExpired);
            }

            var baseUrl = _auth.ApiBaseUrl.TrimEnd('/');

            var lastSync = LastSyncTime;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // ── Push ──────────────────────────────────────────────────────────
            var expenses = await BuildExpensePushItems(db, lastSync, ct);
            var incomes = await BuildIncomePushItems(db, lastSync, ct);
            var subscriptions = await BuildSubscriptionPushItems(db, lastSync, ct);
            var categories = await BuildCategoryPushItems(db, lastSync, ct);

            var pushPayload = new SyncPushRequest(
                expenses, incomes, subscriptions, categories,
                new SyncSettingsDto(
                    _currency.Selected.Code,
                    _localization.CurrentLanguage,
                    _theme.IsDarkMode,
                    _settings.UpdatedAt));

            using var pushRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/sync/push");
            pushRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            pushRequest.Content = JsonContent.Create(pushPayload);
            var pushResponse = await _http.SendAsync(pushRequest, ct);
            if (!pushResponse.IsSuccessStatusCode)
                return await FailFromResponseAsync(pushResponse, "push", ct);

            // ── Pull ──────────────────────────────────────────────────────────
            var sinceParam = lastSync.HasValue ? $"?since={lastSync.Value:O}" : string.Empty;
            using var pullRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/sync/pull{sinceParam}");
            pullRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var pullResponse = await _http.SendAsync(pullRequest, ct);
            if (!pullResponse.IsSuccessStatusCode)
                return await FailFromResponseAsync(pullResponse, "pull", ct);

            var pulled = await pullResponse.Content.ReadFromJsonAsync<SyncPullResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            if (pulled is not null)
            {
                await UpsertPulledData(db, pulled, ct);
                ApplyPulledSettings(pulled.Settings);
            }

            LastSyncTime = DateTime.UtcNow;
            return SyncResult.Success();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Sync cancelled.");
            return SyncResult.Fail(SyncFailureReason.NetworkError);
        }
        catch (HttpRequestException ex)
        {
            // Transport-level: DNS, connection refused, TLS — the request never got a
            // response to check a status code on. A non-success response is handled above,
            // by FailFromResponseAsync, not here.
            _logger.LogError(ex, "Sync failed: the API was unreachable.");
            return SyncResult.Fail(SyncFailureReason.NetworkError);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Sync failed: the API returned a response that could not be parsed.");
            return SyncResult.Fail(SyncFailureReason.ServerError);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Sync failed: pulled data could not be written to the local database.");
            return SyncResult.Fail(SyncFailureReason.LocalDatabaseError);
        }
        finally
        {
            IsSyncing = false;
            SyncStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// A 401 here means the token was accepted by <c>IsLoggedInAsync</c> a moment ago but the
    /// server disagrees now — expiry mid-request, or the server's clock and this device's
    /// having drifted. Everything else the API can return (429, 500) is not something the
    /// user did wrong.
    /// </summary>
    private async Task<SyncResult> FailFromResponseAsync(HttpResponseMessage response, string phase, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        _logger.LogWarning("Sync {Phase} rejected by server: {StatusCode} {Body}.", phase, response.StatusCode, body);

        return SyncResult.Fail(response.StatusCode == HttpStatusCode.Unauthorized
            ? SyncFailureReason.SessionExpired
            : SyncFailureReason.ServerError);
    }

    // ── Push helpers ──────────────────────────────────────────────────────────

    private static async Task<List<SyncExpenseDto>> BuildExpensePushItems(
        AppDbContext db, DateTime? lastSync, CancellationToken ct)
    {
        // IgnoreQueryFilters: soft-deleted rows are exactly what a delete needs to push.
        var query = db.Expenses.IgnoreQueryFilters().AsQueryable();
        if (lastSync.HasValue)
            query = query.Where(e => (e.UpdatedAt != null && e.UpdatedAt > lastSync)
                                  || (e.UpdatedAt == null && e.CreatedAt > lastSync));
        var items = await query.Include(e => e.Category).ToListAsync(ct);
        return items.Select(e => new SyncExpenseDto(
            e.SyncId, e.Description, e.Amount, e.Date,
            e.Category.SyncId, e.Notes, e.CreatedAt, e.UpdatedAt, e.IsDeleted)).ToList();
    }

    private static async Task<List<SyncIncomeDto>> BuildIncomePushItems(
        AppDbContext db, DateTime? lastSync, CancellationToken ct)
    {
        var query = db.Incomes.IgnoreQueryFilters().AsQueryable();
        if (lastSync.HasValue)
            query = query.Where(i => (i.UpdatedAt != null && i.UpdatedAt > lastSync)
                                  || (i.UpdatedAt == null && i.CreatedAt > lastSync));
        var items = await query.ToListAsync(ct);
        return items.Select(i => new SyncIncomeDto(
            i.SyncId, i.Description, i.Amount, i.BillingCycle.ToString(),
            i.StartDate, i.Notes, i.IsActive, i.CreatedAt, i.UpdatedAt, i.IsDeleted)).ToList();
    }

    private static async Task<List<SyncSubscriptionDto>> BuildSubscriptionPushItems(
        AppDbContext db, DateTime? lastSync, CancellationToken ct)
    {
        var query = db.Subscriptions.IgnoreQueryFilters().AsQueryable();
        if (lastSync.HasValue)
            query = query.Where(s => (s.UpdatedAt != null && s.UpdatedAt > lastSync)
                                  || (s.UpdatedAt == null && s.CreatedAt > lastSync));
        var items = await query.Include(s => s.Category).ToListAsync(ct);
        return items.Select(s => new SyncSubscriptionDto(
            s.SyncId, s.Name, s.Amount, s.BillingCycle.ToString(),
            s.StartDate, s.EndDate, s.Category.SyncId, s.Notes, s.Url,
            s.IsActive, s.CreatedAt, s.UpdatedAt, s.IsDeleted)).ToList();
    }

    private static async Task<List<SyncCategoryDto>> BuildCategoryPushItems(
        AppDbContext db, DateTime? lastSync, CancellationToken ct)
    {
        var query = db.Categories.IgnoreQueryFilters().AsQueryable();
        if (lastSync.HasValue)
            query = query.Where(c => (c.UpdatedAt != null && c.UpdatedAt > lastSync)
                                  || (c.UpdatedAt == null && c.CreatedAt > lastSync));
        var items = await query.ToListAsync(ct);
        return items.Select(c => new SyncCategoryDto(
            c.SyncId, c.Name, c.Icon, c.Color, c.IsSystem, c.CreatedAt, c.UpdatedAt, c.IsDeleted)).ToList();
    }

    /// <summary>Applies a pulled delta to the local database.</summary>
    /// <remarks>
    /// Every lookup uses IgnoreQueryFilters. A row that is tombstoned locally is invisible to
    /// a filtered query, so without this the lookup would miss it and insert a duplicate —
    /// and a pulled tombstone could never be matched to the row it is meant to delete.
    /// </remarks>
    private static async Task UpsertPulledData(AppDbContext db, SyncPullResponse pulled, CancellationToken ct)
    {
        foreach (var cat in pulled.Categories ?? [])
        {
            var existing = await db.Categories.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.SyncId == cat.SyncId, ct);
            if (existing is not null)
            {
                existing.Name = cat.Name; existing.Icon = cat.Icon;
                existing.Color = cat.Color; existing.IsSystem = cat.IsSystem;
                existing.IsDeleted = cat.IsDeleted; existing.UpdatedAt = cat.UpdatedAt;
            }
            else
            {
                db.Categories.Add(new Category
                {
                    SyncId = cat.SyncId, Name = cat.Name, Icon = cat.Icon,
                    Color = cat.Color, IsSystem = cat.IsSystem, IsDeleted = cat.IsDeleted,
                    CreatedAt = cat.CreatedAt, UpdatedAt = cat.UpdatedAt
                });
            }
        }
        await db.SaveChangesAsync(ct);

        foreach (var exp in pulled.Expenses ?? [])
        {
            var category = await db.Categories.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.SyncId == exp.CategorySyncId, ct);
            if (category is null) continue;

            var existing = await db.Expenses.IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.SyncId == exp.SyncId, ct);
            if (existing is not null)
            {
                existing.Description = exp.Description; existing.Amount = exp.Amount;
                existing.Date = exp.Date; existing.CategoryId = category.Id;
                existing.Notes = exp.Notes; existing.IsDeleted = exp.IsDeleted;
                existing.UpdatedAt = exp.UpdatedAt;
            }
            else
            {
                // A tombstone for a row this device never had is still worth storing: it stops
                // a later pull from resurrecting it.
                db.Expenses.Add(new Expense
                {
                    SyncId = exp.SyncId, Description = exp.Description, Amount = exp.Amount,
                    Date = exp.Date, CategoryId = category.Id, Notes = exp.Notes,
                    IsDeleted = exp.IsDeleted,
                    CreatedAt = exp.CreatedAt, UpdatedAt = exp.UpdatedAt
                });
            }
        }

        foreach (var inc in pulled.Incomes ?? [])
        {
            var billingCycle = Enum.TryParse<BillingCycle>(inc.BillingCycle, out var bc)
                ? bc : BillingCycle.Monthly;

            var existing = await db.Incomes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.SyncId == inc.SyncId, ct);
            if (existing is not null)
            {
                existing.Description = inc.Description; existing.Amount = inc.Amount;
                existing.BillingCycle = billingCycle; existing.StartDate = inc.StartDate;
                existing.Notes = inc.Notes; existing.IsActive = inc.IsActive;
                existing.IsDeleted = inc.IsDeleted; existing.UpdatedAt = inc.UpdatedAt;
            }
            else
            {
                db.Incomes.Add(new Income
                {
                    SyncId = inc.SyncId, Description = inc.Description, Amount = inc.Amount,
                    BillingCycle = billingCycle, StartDate = inc.StartDate, Notes = inc.Notes,
                    IsActive = inc.IsActive, IsDeleted = inc.IsDeleted,
                    CreatedAt = inc.CreatedAt, UpdatedAt = inc.UpdatedAt
                });
            }
        }

        foreach (var sub in pulled.Subscriptions ?? [])
        {
            var category = await db.Categories.IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.SyncId == sub.CategorySyncId, ct);
            if (category is null) continue;

            var billingCycle = Enum.TryParse<BillingCycle>(sub.BillingCycle, out var bc)
                ? bc : BillingCycle.Monthly;

            var existing = await db.Subscriptions.IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.SyncId == sub.SyncId, ct);
            if (existing is not null)
            {
                existing.Name = sub.Name; existing.Amount = sub.Amount;
                existing.BillingCycle = billingCycle; existing.StartDate = sub.StartDate;
                existing.EndDate = sub.EndDate; existing.CategoryId = category.Id;
                existing.Notes = sub.Notes; existing.Url = sub.Url;
                existing.IsActive = sub.IsActive; existing.IsDeleted = sub.IsDeleted;
                existing.UpdatedAt = sub.UpdatedAt;
            }
            else
            {
                db.Subscriptions.Add(new Subscription
                {
                    SyncId = sub.SyncId, Name = sub.Name, Amount = sub.Amount,
                    BillingCycle = billingCycle, StartDate = sub.StartDate, EndDate = sub.EndDate,
                    CategoryId = category.Id, Notes = sub.Notes, Url = sub.Url,
                    IsActive = sub.IsActive, IsDeleted = sub.IsDeleted,
                    CreatedAt = sub.CreatedAt, UpdatedAt = sub.UpdatedAt
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adopts the account's display preferences when they are newer than this device's.
    /// </summary>
    /// <remarks>
    /// The push above already sent this device's stamp, so anything newer coming back is a
    /// change made elsewhere. The setters stamp the preferences as locally edited, which would
    /// make the next sync push these values straight back as if this device had chosen them —
    /// harmless, but it would beat a concurrent edit on another device purely by being
    /// re-stamped. Restoring the server's stamp afterwards keeps the ordering honest.
    /// </remarks>
    private void ApplyPulledSettings(SyncSettingsDto? settings)
    {
        if (settings is null || settings.UpdatedAt <= _settings.UpdatedAt) return;

        _currency.SetCurrency(settings.Currency);
        _localization.SetLanguage(settings.Language);
        _theme.SetDarkMode(settings.IsDarkMode);

        _settings.UpdatedAt = settings.UpdatedAt;
    }

}
