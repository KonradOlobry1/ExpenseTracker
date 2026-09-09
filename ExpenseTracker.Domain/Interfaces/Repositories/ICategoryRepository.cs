using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces.Repositories;

/// <summary>
/// Category storage. Implemented twice: against the device replica and against the cloud.
/// </summary>
/// <remarks>
/// The two implementations differ in one way that matters to callers: the cloud one scopes
/// every query to the signed-in account, while the device one does not need to — a device
/// holds one account's data at a time, and sign-out clears it.
/// </remarks>
public interface ICategoryRepository
{
    /// <summary>Live categories, name-ordered. Excludes soft-deleted ones.</summary>
    Task<List<Category>> GetAllAsync(CancellationToken ct = default);

    Task<Category> CreateAsync(Category category, CancellationToken ct = default);

    Task<Category> UpdateAsync(Category category, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes the category. Throws for a built-in one; does nothing if the id is unknown.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
