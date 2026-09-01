using System.Globalization;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.ValueObjects;
using ExpenseTracker.Localization;
using ExpenseTracker.Presentation.Services;

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

/// <summary>Currency selection for one browser session.</summary>
public class WebCurrencyService : ICurrencyService
{
    private readonly BrowserPreferences _prefs;

    public WebCurrencyService(BrowserPreferences prefs)
    {
        _prefs = prefs;

        var saved = prefs.Read(PrefKeys.Currency);
        if (saved is not null && Currencies.FirstOrDefault(c => c.Code == saved) is { } restored)
            Selected = restored;
    }

    private static readonly List<CurrencyInfo> Currencies =
    [
        new("USD", "US Dollar",         "$",   "en-US"),
        new("EUR", "Euro",              "€",   "fr-FR"),
        new("GBP", "British Pound",     "£",   "en-GB"),
        new("PLN", "Polish Zloty",      "zł",  "pl-PL"),
        new("JPY", "Japanese Yen",      "¥",   "ja-JP"),
        new("CAD", "Canadian Dollar",   "CA$", "en-CA"),
        new("AUD", "Australian Dollar", "A$",  "en-AU"),
        new("CHF", "Swiss Franc",       "CHF", "de-CH"),
        new("CNY", "Chinese Yuan",      "¥",   "zh-CN"),
        new("INR", "Indian Rupee",      "₹",   "hi-IN"),
        new("BRL", "Brazilian Real",    "R$",  "pt-BR"),
        new("SEK", "Swedish Krona",     "kr",  "sv-SE"),
        new("NOK", "Norwegian Krone",   "kr",  "nb-NO"),
        new("DKK", "Danish Krone",      "kr",  "da-DK"),
        new("MXN", "Mexican Peso",      "$",   "es-MX"),
        new("SGD", "Singapore Dollar",  "S$",  "en-SG"),
    ];

    public IReadOnlyList<CurrencyInfo> Available => Currencies;
    public CurrencyInfo Selected { get; private set; } = Currencies[0];
    public event Action? OnChanged;

    public void SetCurrency(string code)
    {
        var currency = Currencies.FirstOrDefault(c => c.Code == code);
        if (currency is null || currency == Selected) return;

        Selected = currency;
        _prefs.Write(PrefKeys.Currency, code);
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

    public WebThemeService(BrowserPreferences prefs)
    {
        _prefs = prefs;
        IsDarkMode = prefs.Read(PrefKeys.Theme) == "dark";
    }

    public bool IsDarkMode { get; private set; }
    public event Action? OnChanged;

    public void Toggle()
    {
        IsDarkMode = !IsDarkMode;
        _prefs.Write(PrefKeys.Theme, IsDarkMode ? "dark" : "light");
        OnChanged?.Invoke();
    }
}

/// <summary>Language selection for one browser session.</summary>
public class WebLocalizationService : ILocalizationService
{
    private readonly BrowserPreferences _prefs;

    public WebLocalizationService(BrowserPreferences prefs)
    {
        _prefs = prefs;

        var saved = prefs.Read(PrefKeys.Language);
        if (saved is not null && Available.Any(l => l.Code == saved))
        {
            CurrentLanguage = saved;
            Culture = CultureInfo.CreateSpecificCulture(saved);
        }
    }

    public string CurrentLanguage { get; private set; } = "en";
    public CultureInfo Culture { get; private set; } = CultureInfo.CreateSpecificCulture("en");

    public IReadOnlyList<LanguageInfo> Available { get; } =
    [
        new("en", "English", "🇬🇧"),
        new("pl", "Polski",  "🇵🇱"),
    ];

    public event Action? OnChanged;

    public string this[string key]
    {
        get
        {
            if (Translations.All.TryGetValue(CurrentLanguage, out var dict) &&
                dict.TryGetValue(key, out var value))
                return value;

            if (Translations.All.TryGetValue("en", out var en) &&
                en.TryGetValue(key, out var fallback))
                return fallback;

            return key;
        }
    }

    public string Format(string key, params object[] args) => string.Format(this[key], args);

    public void SetLanguage(string code)
    {
        if (CurrentLanguage == code) return;

        CurrentLanguage = code;
        Culture = CultureInfo.CreateSpecificCulture(code);
        _prefs.Write(PrefKeys.Language, code);
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
