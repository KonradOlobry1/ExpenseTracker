using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces.Repositories;

/// <summary>Income storage. See <see cref="ICategoryRepository"/> on the two implementations.</summary>
public interface IIncomeRepository
{
    /// <summary>Income entries, newest start date first. Excludes soft-deleted ones.</summary>
    /// <param name="activeOnly">Defaults to false: the list is a history, not just what pays now.</param>
    Task<List<Income>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default);

    Task<Income> CreateAsync(Income income, CancellationToken ct = default);

    Task<Income> UpdateAsync(Income income, CancellationToken ct = default);

    /// <summary>Soft-deletes the entry. Does nothing if the id is unknown.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Only entries currently paying out — what the monthly-equivalent total is built from.
    /// </summary>
    Task<List<Income>> GetActiveAsync(CancellationToken ct = default);
}
