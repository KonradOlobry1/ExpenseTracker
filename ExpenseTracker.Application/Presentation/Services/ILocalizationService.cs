using System.Globalization;
using ExpenseTracker.Domain.ValueObjects;

namespace ExpenseTracker.Presentation.Services;

public interface ILocalizationService
{
    string this[string key] { get; }
    string Format(string key, params object[] args);
    string CurrentLanguage { get; }

    /// <summary>Specific culture for the selected language. Drives dates and numbers.</summary>
    CultureInfo Culture { get; }
    IReadOnlyList<LanguageInfo> Available { get; }
    void SetLanguage(string code);
    event Action? OnChanged;
}
