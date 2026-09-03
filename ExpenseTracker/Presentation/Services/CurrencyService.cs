using System.Globalization;
using ExpenseTracker.Domain.ValueObjects;

namespace ExpenseTracker.Presentation.Services;

public class CurrencyService : ICurrencyService
{
    private const string PrefsKey = "selected_currency";

    public IReadOnlyList<CurrencyInfo> Available => Currencies.All;
    public CurrencyInfo Selected { get; private set; }
    public event Action? OnChanged;

    public CurrencyService()
    {
        Selected = Currencies.FromCodeOrDefault(Preferences.Get(PrefsKey, Currencies.Default.Code));
    }

    public void SetCurrency(string code)
    {
        var currency = Currencies.All.FirstOrDefault(c => c.Code == code);
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
