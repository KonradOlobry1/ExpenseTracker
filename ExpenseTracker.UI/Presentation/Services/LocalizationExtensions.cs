using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Presentation.Services;

public static class LocalizationExtensions
{
    /// <summary>
    /// Resolves a category display name. System categories have a
    /// <c>Category_{Name}</c> translation key; user-created categories have none
    /// and fall back to their raw name.
    /// </summary>
    public static string Category(this ILocalizationService l, string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;

        var key = $"Category_{name}";
        var value = l[key];

        // The indexer echoes the key back when no translation exists.
        return value == key ? name : value;
    }

    /// <summary>
    /// Resolves a billing cycle's display name. The enum member names are the storage and
    /// wire format, so they must not be translated at the source — only on the way out.
    /// </summary>
    public static string Cycle(this ILocalizationService l, BillingCycle cycle)
    {
        var key = $"Cycle_{cycle}";
        var value = l[key];
        return value == key ? cycle.ToString() : value;
    }

    /// <summary>
    /// Orders categories by their translated name. The repository sorts by the raw
    /// English name, which looks arbitrary once the names are localized.
    /// </summary>
    public static List<Category> SortCategories(this ILocalizationService l, IEnumerable<Category> categories)
    {
        return categories
            .OrderBy(c => l.Category(c.Name), StringComparer.Create(l.Culture, ignoreCase: true))
            .ToList();
    }
}
