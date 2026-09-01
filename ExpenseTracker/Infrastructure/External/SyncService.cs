using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Application.Interfaces;
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

    public SyncService(
        IAuthService auth,
        IDbContextFactory<AppDbContext> dbFactory,
        HttpClient http,
        ILogger<SyncService> logger,
        ICurrencyService currency,
        ILocalizationService localization,
        IThemeService theme)
    {
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
            var ticks = Preferences.Default.Get<long>(LastSyncKey, 0);
            return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
        }
        private set
        {
            if (value.HasValue)
                Preferences.Default.Set(LastSyncKey, value.Value.Ticks);
            else
                Preferences.Default.Remove(LastSyncKey);
        }
    }

    public bool IsSyncing { get; private set; }

    public event Action? SyncStateChanged;

    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        if (!await _auth.IsLoggedInAsync()) return false;

        IsSyncing = true;
        SyncStateChanged?.Invoke();

        try
        {
            var token = await _auth.GetTokenAsync();
            if (token is null)
            {
                _logger.LogWarning("Sync aborted: no stored auth token.");
                return false;
            }

            var baseUrl = _auth.ApiBaseUrl?.TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl))
            {
                _logger.LogWarning("Sync aborted: no API base URL configured.");
                return false;
            }

            var lastSync = LastSyncTime;

            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // ── Push ──────────────────────────────────────────────────────────
            var expenses = await BuildExpensePushItems(db, lastSync, ct);
            var incomes = await BuildIncomePushItems(db, lastSync, ct);
            var subscriptions = await BuildSubscriptionPushItems(db, lastSync, ct);
            var categories = await BuildCategoryPushItems(db, lastSync, ct);

            var pushPayload = new PushPayload(
                expenses, incomes, subscriptions, categories,
                new SyncSettings(
                    _currency.Selected.Code,
                    _localization.CurrentLanguage,
                    _theme.IsDarkMode,
                    LocalSettings.UpdatedAt));

            using var pushRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/sync/push");
            pushRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            pushRequest.Content = JsonContent.Create(pushPayload);
            var pushResponse = await _http.SendAsync(pushRequest, ct);
            pushResponse.EnsureSuccessStatusCode();

            // ── Pull ──────────────────────────────────────────────────────────
            var sinceParam = lastSync.HasValue ? $"?since={lastSync.Value:O}" : string.Empty;
            using var pullRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/sync/pull{sinceParam}");
            pullRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var pullResponse = await _http.SendAsync(pullRequest, ct);
            pullResponse.EnsureSuccessStatusCode();

            var pulled = await pullResponse.Content.ReadFromJsonAsync<PullResponse>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, ct);

            if (pulled is not null)
            {
                await UpsertPulledData(db, pulled, ct);
                ApplyPulledSettings(pulled.Settings);
            }

            LastSyncTime = DateTime.UtcNow;
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Sync cancelled.");
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Sync failed: the API was unreachable or returned an error.");
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Sync failed: the API returned a response that could not be parsed.");
            return false;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Sync failed: pulled data could not be written to the local database.");
            return false;
        }
        finally
        {
            IsSyncing = false;
            SyncStateChanged?.Invoke();
        }
    }

    // ── Push helpers ──────────────────────────────────────────────────────────

    private static async Task<List<PushExpense>> BuildExpensePushItems(
        AppDbContext db, DateTime? lastSync, CancellationToken ct)
    {
        // IgnoreQueryFilters: soft-deleted rows are exactly what a delete needs to push.
        var query = db.Expenses.IgnoreQueryFilters().AsQueryable();
        if (lastSync.HasValue)
            query = query.Where(e => (e.UpdatedAt != null && e.UpdatedAt > lastSync)
                                  || (e.UpdatedAt == null && e.CreatedAt > lastSync));
        var items = await query.Include(e => e.Category).ToListAsync(ct);
        return items.Select(e => new PushExpense(
            e.SyncId, e.Description, e.Amount, e.Date,
            e.Category.SyncId, e.Notes, e.CreatedAt, e.UpdatedAt, e.IsDeleted)).ToList();
    }

    private static async Task<List<PushIncome>> BuildIncomePushItems(
        AppDbContext db, DateTime? lastSync, CancellationToken ct)
    {
        var query = db.Incomes.IgnoreQueryFilters().AsQueryable();
        if (lastSync.HasValue)
            query = query.Where(i => (i.UpdatedAt != null && i.UpdatedAt > lastSync)
                                  || (i.UpdatedAt == null && i.CreatedAt > lastSync));
        var items = await query.ToListAsync(ct);
        return items.Select(i => new PushIncome(
            i.SyncId, i.Description, i.Amount, i.BillingCycle.ToString(),
            i.StartDate, i.Notes, i.IsActive, i.CreatedAt, i.UpdatedAt, i.IsDeleted)).ToList();
    }

    private static async Task<List<PushSubscription>> BuildSubscriptionPushItems(
        AppDbContext db, DateTime? lastSync, CancellationToken ct)
    {
        var query = db.Subscriptions.IgnoreQueryFilters().AsQueryable();
        if (lastSync.HasValue)
            query = query.Where(s => (s.UpdatedAt != null && s.UpdatedAt > lastSync)
                                  || (s.UpdatedAt == null && s.CreatedAt > lastSync));
        var items = await query.Include(s => s.Category).ToListAsync(ct);
        return items.Select(s => new PushSubscription(
            s.SyncId, s.Name, s.Amount, s.BillingCycle.ToString(),
            s.StartDate, s.EndDate, s.Category.SyncId, s.Notes, s.Url,
            s.IsActive, s.CreatedAt, s.UpdatedAt, s.IsDeleted)).ToList();
    }

    private static async Task<List<PushCategory>> BuildCategoryPushItems(
        AppDbContext db, DateTime? lastSync, CancellationToken ct)
    {
        var query = db.Categories.IgnoreQueryFilters().AsQueryable();
        if (lastSync.HasValue)
            query = query.Where(c => (c.UpdatedAt != null && c.UpdatedAt > lastSync)
                                  || (c.UpdatedAt == null && c.CreatedAt > lastSync));
        var items = await query.ToListAsync(ct);
        return items.Select(c => new PushCategory(
            c.SyncId, c.Name, c.Icon, c.Color, c.IsSystem, c.CreatedAt, c.UpdatedAt, c.IsDeleted)).ToList();
    }

    /// <summary>Applies a pulled delta to the local database.</summary>
    /// <remarks>
    /// Every lookup uses IgnoreQueryFilters. A row that is tombstoned locally is invisible to
    /// a filtered query, so without this the lookup would miss it and insert a duplicate —
    /// and a pulled tombstone could never be matched to the row it is meant to delete.
    /// </remarks>
    private static async Task UpsertPulledData(AppDbContext db, PullResponse pulled, CancellationToken ct)
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
    private void ApplyPulledSettings(SyncSettings? settings)
    {
        if (settings is null || settings.UpdatedAt <= LocalSettings.UpdatedAt) return;

        _currency.SetCurrency(settings.Currency);
        _localization.SetLanguage(settings.Language);
        _theme.SetDarkMode(settings.IsDarkMode);

        LocalSettings.UpdatedAt = settings.UpdatedAt;
    }

    // ── Push DTOs ─────────────────────────────────────────────────────────────

    private record PushPayload(
        List<PushExpense> Expenses,
        List<PushIncome> Incomes,
        List<PushSubscription> Subscriptions,
        List<PushCategory> Categories,
        SyncSettings Settings);

    private record PushExpense(Guid SyncId, string Description, decimal Amount, DateTime Date,
        Guid CategorySyncId, string? Notes, DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

    private record PushIncome(Guid SyncId, string Description, decimal Amount, string BillingCycle,
        DateTime StartDate, string? Notes, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

    private record PushSubscription(Guid SyncId, string Name, decimal Amount, string BillingCycle,
        DateTime StartDate, DateTime? EndDate, Guid CategorySyncId, string? Notes, string? Url,
        bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

    private record PushCategory(Guid SyncId, string Name, string Icon, string Color,
        bool IsSystem, DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

    // ── Pull DTOs ─────────────────────────────────────────────────────────────

    private record PullResponse(
        List<PulledExpense>? Expenses,
        List<PulledIncome>? Incomes,
        List<PulledSubscription>? Subscriptions,
        List<PulledCategory>? Categories,
        DateTime ServerTime,
        SyncSettings? Settings);

    /// <summary>Account-wide display preferences. One per account, replaced wholesale.</summary>
    private record SyncSettings(string Currency, string Language, bool IsDarkMode, DateTime UpdatedAt);

    private record PulledExpense(Guid SyncId, string Description, decimal Amount, DateTime Date,
        Guid CategorySyncId, string? Notes, DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

    private record PulledIncome(Guid SyncId, string Description, decimal Amount, string BillingCycle,
        DateTime StartDate, string? Notes, bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

    private record PulledSubscription(Guid SyncId, string Name, decimal Amount, string BillingCycle,
        DateTime StartDate, DateTime? EndDate, Guid CategorySyncId, string? Notes, string? Url,
        bool IsActive, DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);

    private record PulledCategory(Guid SyncId, string Name, string Icon, string Color,
        bool IsSystem, DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted);
}
