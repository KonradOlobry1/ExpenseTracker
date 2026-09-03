namespace ExpenseTracker.Domain.ValueObjects;

/// <summary>
/// The currencies the app can display amounts in.
/// </summary>
/// <remarks>
/// One list, in the domain, because the device and the web both need it and a currency the
/// two disagree about is a currency that breaks formatting on one of them. It used to be
/// duplicated verbatim in CurrencyService and WebCurrencyService, and the two copies had
/// already drifted apart in whitespace.
///
/// <see cref="CurrencyInfo.CultureName"/> is the culture money is formatted with, so symbol
/// placement and separators follow that currency's own conventions rather than the UI
/// language.
/// </remarks>
public static class Currencies
{
    public static IReadOnlyList<CurrencyInfo> All { get; } =
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

    /// <summary>The default every client falls back to.</summary>
    public static CurrencyInfo Default => All[0];

    /// <summary>
    /// The currency with this code, or <see cref="Default"/>. Codes now arrive from the
    /// account rather than only from this build's own list, so an unknown one has to resolve
    /// to something rather than throw.
    /// </summary>
    public static CurrencyInfo FromCodeOrDefault(string? code)
        => All.FirstOrDefault(c => c.Code == code) ?? Default;
}
