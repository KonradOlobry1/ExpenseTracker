namespace ExpenseTracker.Domain.ValueObjects;

/// <summary>
/// The UI languages the app ships translations for.
/// </summary>
/// <remarks>
/// Adding one here is not enough on its own — <c>Translations</c> needs a matching dictionary,
/// or every key falls back to English.
/// </remarks>
public static class Languages
{
    public const string DefaultCode = "en";

    public static IReadOnlyList<LanguageInfo> All { get; } =
    [
        new("en", "English", "🇬🇧"),
        new("pl", "Polski",  "🇵🇱"),
    ];

    /// <summary>
    /// Whether a code is one this build can actually display. Language codes now arrive from
    /// the account, and an unknown one handed to CultureInfo.CreateSpecificCulture throws.
    /// </summary>
    public static bool IsSupported(string? code) => All.Any(l => l.Code == code);

    public static string CodeOrDefault(string? code) => IsSupported(code) ? code! : DefaultCode;
}
