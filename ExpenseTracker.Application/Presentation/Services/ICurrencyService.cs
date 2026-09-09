using ExpenseTracker.Domain.ValueObjects;

namespace ExpenseTracker.Presentation.Services;

/// <summary>
/// The display currency, and formatting amounts in it.
/// </summary>
/// <remarks>
/// Independent of the UI language on purpose: someone reading the app in English may well be
/// spending złoty. Amounts format in the culture of the <em>currency</em>, so symbol placement
/// and separators follow the money rather than the interface.
///
/// This changes presentation only. Stored amounts are plain decimals and are never converted —
/// switching currency relabels the figures, it does not run an exchange rate over them.
/// </remarks>
public interface ICurrencyService
{
    CurrencyInfo Selected { get; }

    /// <summary>The sixteen supported currencies.</summary>
    IReadOnlyList<CurrencyInfo> Available { get; }

    /// <summary>
    /// Selects a currency by ISO code, persists it, and raises <see cref="OnChanged"/>. An
    /// unknown code is ignored rather than throwing — the value can arrive from a synced
    /// account written by a newer build.
    /// </summary>
    void SetCurrency(string code);

    /// <summary>Formats an amount in the selected currency's own culture.</summary>
    string Format(decimal amount, string format = "C2");

    /// <summary>Raised after the selection changes, so open pages can re-render.</summary>
    event Action? OnChanged;
}
