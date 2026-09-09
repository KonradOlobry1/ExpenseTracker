using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces.Repositories;

/// <summary>
/// The expense list filter. Every field is optional; the ones that are set are combined with
/// AND, and an all-null filter matches everything.
/// </summary>
/// <param name="SearchText">Matched against both the description and the notes.</param>
public record ExpenseFilter(
    DateTime? From,
    DateTime? To,
    int? CategoryId,
    decimal? MinAmount,
    decimal? MaxAmount,
    string? SearchText
);

/// <summary>
/// One page of results together with how many rows matched in total — not how many are in
/// <see cref="Items"/>.
/// </summary>
public record PagedResult<T>(List<T> Items, int TotalCount);

/// <summary>
/// Expense storage. See <see cref="ICategoryRepository"/> on the two implementations.
/// </summary>
public interface IExpenseRepository
{
    /// <summary>Every live expense, newest first, with its category loaded.</summary>
    Task<List<Expense>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Live expenses dated within the given month, newest first.</summary>
    Task<List<Expense>> GetByMonthAsync(int year, int month, CancellationToken ct = default);

    Task<Expense> CreateAsync(Expense expense, CancellationToken ct = default);

    Task<Expense> UpdateAsync(Expense expense, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes the expense, leaving a tombstone for other devices. Does nothing if the id
    /// is unknown.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Totals for one year keyed by month number. Months with nothing spent are absent rather
    /// than zero.
    /// </summary>
    Task<Dictionary<int, decimal>> GetMonthlyTotalsAsync(int year, CancellationToken ct = default);

    /// <summary>
    /// One month's spending keyed by category name. Categories with nothing spent are absent.
    /// </summary>
    Task<Dictionary<string, decimal>> GetCategoryTotalsAsync(int year, int month, CancellationToken ct = default);

    /// <summary>One page of matching expenses, plus the total number that matched.</summary>
    /// <param name="page">Zero-based.</param>
    Task<PagedResult<Expense>> GetPagedAsync(ExpenseFilter filter, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// The sum of every expense matching the filter, computed in the database.
    /// </summary>
    /// <remarks>
    /// Deliberately not derivable from <see cref="GetPagedAsync"/>: summing a returned page
    /// would show the total of one screenful while the filter matched hundreds of rows.
    /// </remarks>
    Task<decimal> GetFilteredSumAsync(ExpenseFilter filter, CancellationToken ct = default);
}
