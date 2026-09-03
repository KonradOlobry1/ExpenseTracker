using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.ValueObjects;
using ExpenseTracker.Localization;
using ExpenseTracker.Presentation.Services;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Web;

// Web-side implementations of the UI service interfaces the shared components inject.
// The MAUI versions persist to Preferences; these persist to cookies via BrowserPreferences,
// which reads from the request (so the right value is there before the first render) and
// writes through JS (a circuit cannot set response headers).

internal static class PrefKeys
{
    public const string Currency = "pref.currency";
    public const string Theme    = "pref.theme";
    public const string Language = "pref.lang";
}

/// <summary>
/// Mirrors a preference change onto the signed-in account, so it follows the user to their
/// phone and to any other browser.
/// </summary>
/// <remarks>
/// The cookie is still what the next render reads — it arrives with the request, whereas a
/// database round-trip cannot be made from a synchronous property getter. The account row is
/// the durable copy: <c>Account/Login</c> writes the cookies from it on sign-in, so a browser
/// that has never seen this account still starts with the right values.
/// </remarks>
public class AccountSettingsWriter(
    IDbContextFactory<ApiDbContext> dbFactory,
    IHttpContextAccessor accessor,
    ILogger<AccountSettingsWriter> logger)
{
    public void Write(Action<AppUser> change) => _ = WriteAsync(change);

    private async Task WriteAsync(Action<AppUser> change)
    {
        var userId = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return;   // the login page renders before there is an account

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user is null) return;

            change(user);
            user.SettingsUpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (DbException ex)
        {
            // Fire-and-forget, like the cookie write it accompanies: losing the durable copy
            // is not worth faulting the click the user just made.
            logger.LogError(ex, "Could not persist a preference change to the account.");
        }
    }
}

/// <summary>Currency selection for one browser session.</summary>
public class WebCurrencyService : ICurrencyService
{
    private readonly BrowserPreferences _prefs;
    private readonly AccountSettingsWriter _account;

    public WebCurrencyService(BrowserPreferences prefs, AccountSettingsWriter account)
    {
        _prefs = prefs;
        _account = account;

        Selected = Currencies.FromCodeOrDefault(prefs.Read(PrefKeys.Currency));
    }


    public IReadOnlyList<CurrencyInfo> Available => Currencies.All;
    public CurrencyInfo Selected { get; private set; } = Currencies.Default;
    public event Action? OnChanged;

    public void SetCurrency(string code)
    {
        var currency = Currencies.All.FirstOrDefault(c => c.Code == code);
        if (currency is null || currency == Selected) return;

        Selected = currency;
        _prefs.Write(PrefKeys.Currency, code);
        _account.Write(u => u.Currency = code);
        OnChanged?.Invoke();
    }

    // Money uses the currency's own culture so symbol and separators match its conventions;
    // dates and plain numbers follow the UI language instead. GetCultureInfo is cached.
    public string Format(decimal amount, string format = "C2")
        => amount.ToString(format, CultureInfo.GetCultureInfo(Selected.CultureName));
}

/// <summary>Dark-mode toggle for one browser session.</summary>
public class WebThemeService : IThemeService
{
    private readonly BrowserPreferences _prefs;
    private readonly AccountSettingsWriter _account;

    public WebThemeService(BrowserPreferences prefs, AccountSettingsWriter account)
    {
        _prefs = prefs;
        _account = account;
        IsDarkMode = prefs.Read(PrefKeys.Theme) == "dark";
    }

    public bool IsDarkMode { get; private set; }
    public event Action? OnChanged;

    public void Toggle() => SetDarkMode(!IsDarkMode);

    public void SetDarkMode(bool isDarkMode)
    {
        if (isDarkMode == IsDarkMode) return;

        IsDarkMode = isDarkMode;
        _prefs.Write(PrefKeys.Theme, isDarkMode ? "dark" : "light");
        _account.Write(u => u.IsDarkMode = isDarkMode);
        OnChanged?.Invoke();
    }
}

/// <summary>Language selection for one browser session.</summary>
public class WebLocalizationService : ILocalizationService
{
    private readonly BrowserPreferences _prefs;
    private readonly AccountSettingsWriter _account;

    public WebLocalizationService(BrowserPreferences prefs, AccountSettingsWriter account)
    {
        _prefs = prefs;
        _account = account;

        CurrentLanguage = Languages.CodeOrDefault(prefs.Read(PrefKeys.Language));
        Culture = CultureInfo.CreateSpecificCulture(CurrentLanguage);
    }

    public string CurrentLanguage { get; private set; } = Languages.DefaultCode;
    public CultureInfo Culture { get; private set; } =
        CultureInfo.CreateSpecificCulture(Languages.DefaultCode);

    public IReadOnlyList<LanguageInfo> Available => Languages.All;

    public event Action? OnChanged;

    public string this[string key]
    {
        get
        {
            if (Translations.All.TryGetValue(CurrentLanguage, out var dict) &&
                dict.TryGetValue(key, out var value))
                return value;

            if (Translations.All.TryGetValue(Languages.DefaultCode, out var en) &&
                en.TryGetValue(key, out var fallback))
                return fallback;

            return key;
        }
    }

    public string Format(string key, params object[] args) => string.Format(this[key], args);

    public void SetLanguage(string code)
    {
        // Unknown codes are ignored rather than passed to CreateSpecificCulture: the value can
        // now originate from another device's settings, not only from this app's own list.
        if (CurrentLanguage == code || !Languages.IsSupported(code)) return;

        CurrentLanguage = code;
        Culture = CultureInfo.CreateSpecificCulture(code);
        _prefs.Write(PrefKeys.Language, code);
        _account.Write(u => u.Language = code);
        OnChanged?.Invoke();
    }
}

/// <summary>
/// The web app reads and writes the cloud database directly, so there is nothing to sync.
/// </summary>
public class NoOpSyncService : ISyncService
{
    public bool IsSyncing => false;
    public DateTime? LastSyncTime => null;
    public event Action? SyncStateChanged;

    public Task<bool> SyncAsync(CancellationToken ct = default)
    {
        SyncStateChanged?.Invoke();   // keeps the compiler honest about the unused event
        return Task.FromResult(true);
    }
}
