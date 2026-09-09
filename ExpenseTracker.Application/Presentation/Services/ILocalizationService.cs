using System.Globalization;
using ExpenseTracker.Domain.ValueObjects;

namespace ExpenseTracker.Presentation.Services;

/// <summary>
/// UI language: translated strings and the culture that formats dates.
/// </summary>
/// <remarks>
/// Independent of the display currency — see <see cref="ICurrencyService"/>. Translations are
/// compiled in rather than loaded from resource files, so a missing key is a code change, not
/// a deployment one.
/// </remarks>
public interface ILocalizationService
{
    /// <summary>
    /// The translation for a key. An unknown key returns the key itself, so a missing string
    /// shows up as visible nonsense in the UI rather than an empty gap or an exception.
    /// </summary>
    string this[string key] { get; }

    /// <summary>The translation for a key with <paramref name="args"/> substituted into it.</summary>
    string Format(string key, params object[] args);

    string CurrentLanguage { get; }

    /// <summary>Specific culture for the selected language. Drives dates and numbers.</summary>
    CultureInfo Culture { get; }

    IReadOnlyList<LanguageInfo> Available { get; }

    /// <summary>
    /// Selects a language by code, persists it, and raises <see cref="OnChanged"/>. An unknown
    /// code is ignored rather than throwing, for the same reason as the currency.
    /// </summary>
    void SetLanguage(string code);

    /// <summary>Raised after the language changes, so open pages can re-render.</summary>
    event Action? OnChanged;
}
