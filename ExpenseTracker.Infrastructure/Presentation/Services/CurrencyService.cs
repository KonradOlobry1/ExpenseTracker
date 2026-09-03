using System.Globalization;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.ValueObjects;

namespace ExpenseTracker.Presentation.Services;

public class CurrencyService : ICurrencyService
{
    private const string PrefKey = "selected_currency";

    private readonly IPreferenceStore _prefs;
    private readonly LocalSettings _settings;

    public CurrencyService(IPreferenceStore prefs, LocalSettings settings)
    {
        _prefs = prefs;
        _settings = settings;
        Selected = Currencies.FromCodeOrDefault(prefs.Get(PrefKey, Currencies.Default.Code));
    }

    public IReadOnlyList<CurrencyInfo> Available => Currencies.All;
    public CurrencyInfo Selected { get; private set; }
    public event Action? OnChanged;

    public void SetCurrency(string code)
    {
        var currency = Currencies.All.FirstOrDefault(c => c.Code == code);
        if (currency is null || currency == Selected) return;

        Selected = currency;
        _prefs.Set(PrefKey, code);
        _settings.Touch();
        OnChanged?.Invoke();
    }

    // Money is formatted with the currency's own culture so the symbol, placement and
    // separators match that currency's conventions. Dates and plain numbers follow the
    // UI language instead — see LocalizationService.
    // GetCultureInfo is cached; `new CultureInfo` is not, and Format runs per grid cell.
    public string Format(decimal amount, string format = "C2")
        => amount.ToString(format, CultureInfo.GetCultureInfo(Selected.CultureName));
}
