using System.Globalization;
using ExpenseTracker.Domain.ValueObjects;
using ExpenseTracker.Localization;

namespace ExpenseTracker.Presentation.Services;

public class LocalizationService : ILocalizationService
{
    private const string PrefKey = "app_language";

    public string CurrentLanguage { get; private set; }

    public CultureInfo Culture { get; private set; }

    public IReadOnlyList<LanguageInfo> Available { get; } =
    [
        new("en", "English", "🇬🇧"),
        new("pl", "Polski",  "🇵🇱"),
    ];

    public event Action? OnChanged;

    public LocalizationService()
    {
        var saved = Preferences.Default.Get(PrefKey, "en");
        CurrentLanguage = Available.Any(l => l.Code == saved) ? saved : "en";
        Culture = CultureInfo.CreateSpecificCulture(CurrentLanguage);
        ApplyCulture();
    }

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

    public string Format(string key, params object[] args)
        => string.Format(this[key], args);

    public void SetLanguage(string code)
    {
        // An unknown code would make CreateSpecificCulture throw and take the UI with it.
        // Settings now arrive from the account, so the value is no longer only ever one this
        // build put in Preferences itself.
        if (CurrentLanguage == code || Available.All(l => l.Code != code)) return;

        CurrentLanguage = code;
        Culture = CultureInfo.CreateSpecificCulture(code);
        Preferences.Default.Set(PrefKey, code);
        LocalSettings.Touch();
        ApplyCulture();
        OnChanged?.Invoke();
    }

    // Dates and numbers follow the UI language. Money is formatted separately by
    // ICurrencyService using the selected currency's own culture.
    private void ApplyCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.CurrentUICulture = Culture;
    }
}
