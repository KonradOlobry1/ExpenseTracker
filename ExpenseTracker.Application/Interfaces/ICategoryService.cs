using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// Spending categories for the current account.
/// </summary>
/// <remarks>
/// Seven built-in categories exist on every account, with fixed <c>SyncId</c> values shared by
/// the device seed and the cloud, so both sides agree they are the same seven rather than
/// creating duplicates on a first sync.
/// </remarks>
public interface ICategoryService
{
    /// <summary>Live categories, name-ordered. Excludes soft-deleted ones.</summary>
    Task<List<Category>> GetAllAsync(CancellationToken ct = default);

    Task<Category> CreateAsync(Category category, CancellationToken ct = default);

    Task<Category> UpdateAsync(Category category, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes the category, leaving a tombstone for other devices to pull.
    /// </summary>
    /// <remarks>
    /// Throws for a built-in category: expenses reference categories, and the seven are the
    /// set every account is guaranteed to have. Does nothing if the id is unknown.
    /// </remarks>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
