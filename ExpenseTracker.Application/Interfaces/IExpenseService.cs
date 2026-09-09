using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;

namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// Expenses for the current account, and the aggregates the dashboard and analytics draw from.
/// </summary>
/// <remarks>
/// Every read here excludes soft-deleted rows, and rows whose category has been deleted. That
/// filtering is the repository's job rather than the caller's, because the two heads disagree
/// about how it happens: the device context applies a query filter, while the cloud context
/// deliberately has none — pull has to be able to return tombstones so deletions propagate.
/// </remarks>
public interface IExpenseService
{
    /// <summary>Every live expense, newest first, with its category loaded.</summary>
    Task<List<Expense>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Live expenses dated within the given month, newest first.</summary>
    Task<List<Expense>> GetByMonthAsync(int year, int month, CancellationToken ct = default);

    Task<Expense> CreateAsync(Expense expense, CancellationToken ct = default);

    Task<Expense> UpdateAsync(Expense expense, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes the expense, leaving a tombstone so the deletion reaches other devices.
    /// Does nothing if the id is unknown.
    /// </summary>
    /// <remarks>
    /// A hard delete could not be communicated: the next device to push would simply re-send
    /// the row it still has, and the expense would come back.
    /// </remarks>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Totals for one year keyed by month number. Months with no expenses are absent rather
    /// than zero, so callers drawing a chart supply their own baseline.
    /// </summary>
    Task<Dictionary<int, decimal>> GetMonthlyTotalsAsync(int year, CancellationToken ct = default);

    /// <summary>
    /// One month's spending keyed by category name. Categories with nothing spent are absent.
    /// </summary>
    Task<Dictionary<string, decimal>> GetCategoryTotalsAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// One page of expenses matching the filter, plus the total number that matched.
    /// </summary>
    /// <param name="page">Zero-based.</param>
    Task<PagedResult<Expense>> GetPagedAsync(ExpenseFilter filter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// The sum of <em>every</em> expense matching the filter, not just the page on screen.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="GetPagedAsync"/> on purpose, and computed in the database.
    /// Summing the returned page instead would silently show the total of ten rows while the
    /// filter matched hundreds.
    /// </remarks>
    Task<decimal> GetFilteredSumAsync(ExpenseFilter filter, CancellationToken ct = default);
}
