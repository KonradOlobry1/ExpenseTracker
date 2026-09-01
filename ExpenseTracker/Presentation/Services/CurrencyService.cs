using System.Globalization;
using ExpenseTracker.Domain.ValueObjects;

namespace ExpenseTracker.Presentation.Services;

public class CurrencyService : ICurrencyService
{
    private const string PrefsKey = "selected_currency";

    private static readonly List<CurrencyInfo> _currencies =
    [
        new("USD", "US Dollar",         "$",   "en-US"),
        new("EUR", "Euro",              "€",   "fr-FR"),
        new("GBP", "British Pound",     "£",   "en-GB"),
        new("PLN", "Polish Zloty",       "zł",  "pl-PL"),
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

    public IReadOnlyList<CurrencyInfo> Available => _currencies;
    public CurrencyInfo Selected { get; private set; }
    public event Action? OnChanged;

    public CurrencyService()
    {
        var saved = Preferences.Get(PrefsKey, "USD");
        Selected = _currencies.FirstOrDefault(c => c.Code == saved) ?? _currencies[0];
    }

    public void SetCurrency(string code)
    {
        var currency = _currencies.FirstOrDefault(c => c.Code == code);
        if (currency is null || currency == Selected) return;

        Selected = currency;
        Preferences.Set(PrefsKey, code);
        LocalSettings.Touch();
        OnChanged?.Invoke();
    }

    // Money is formatted with the currency's own culture so the symbol, placement and
    // separators match that currency's conventions. Dates and plain numbers follow the
    // UI language instead — see LocalizationService.
    // GetCultureInfo is cached; `new CultureInfo` is not, and Format runs per grid cell.
    public string Format(decimal amount, string format = "C2")
        => amount.ToString(format, CultureInfo.GetCultureInfo(Selected.CultureName));
}
