using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data;

/// <summary>
/// Gives a new account the same built-in categories a freshly installed device gets.
/// </summary>
/// <remarks>
/// On a device these come from AppDbContext's HasData seed; the cloud has no equivalent, so
/// an account created through the web UI would otherwise have none — and no category means
/// no expense can be created at all.
///
/// The SyncIds deliberately match the device seed. Sync matches rows by SyncId, so using the
/// same fixed values means a phone and the web agree these are the *same* seven categories
/// rather than creating a duplicate set on the first sync.
/// </remarks>
public static class DefaultCategories
{
    private static readonly (string SyncId, string Name, string Icon, string Color)[] Seed =
    [
        ("11111111-0000-0000-0000-000000000001", "Food",          "restaurant",     "#F44336"),
        ("11111111-0000-0000-0000-000000000002", "Transport",     "directions_car", "#2196F3"),
        ("11111111-0000-0000-0000-000000000003", "Housing",       "home",           "#4CAF50"),
        ("11111111-0000-0000-0000-000000000004", "Health",        "favorite",       "#E91E63"),
        ("11111111-0000-0000-0000-000000000005", "Entertainment", "movie",          "#9C27B0"),
        ("11111111-0000-0000-0000-000000000006", "Utilities",     "bolt",           "#FF9800"),
        ("11111111-0000-0000-0000-000000000007", "Other",         "more_horiz",     "#607D8B"),
    ];

    /// <summary>Adds any missing built-in categories for the account. Safe to call repeatedly.</summary>
    public static async Task EnsureForUserAsync(ApiDbContext db, string userId, CancellationToken ct = default)
    {
        var existing = await db.Categories
            .Where(c => c.UserId == userId)
            .Select(c => c.SyncId)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var added = false;

        foreach (var (syncId, name, icon, color) in Seed)
        {
            var id = Guid.Parse(syncId);
            if (existing.Contains(id)) continue;

            db.Categories.Add(new Category
            {
                SyncId = id,
                UserId = userId,
                Name = name,
                Icon = icon,
                Color = color,
                IsSystem = true,
                CreatedAt = now,
                ClientUpdatedAt = now
            });
            added = true;
        }

        if (added) await db.SaveChangesAsync(ct);
    }
}
