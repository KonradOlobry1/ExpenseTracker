using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// Recurring income for the current account.
/// </summary>
/// <remarks>
/// Income carries a billing cycle exactly as subscriptions do — a salary is monthly, a
/// dividend may be quarterly — so the two are comparable only once both are normalised to a
/// monthly equivalent.
/// </remarks>
public interface IIncomeService
{
    /// <summary>
    /// Income entries, newest start date first. Excludes soft-deleted ones.
    /// </summary>
    /// <param name="activeOnly">
    /// Defaults to <c>false</c> here — the income list is a history, and an ended salary is
    /// still worth seeing. Note <see cref="ISubscriptionService.GetAllAsync"/> defaults the
    /// other way, because a cancelled subscription is noise rather than history.
    /// </param>
    Task<List<Income>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default);

    Task<Income> CreateAsync(Income income, CancellationToken ct = default);

    Task<Income> UpdateAsync(Income income, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes the entry, leaving a tombstone for other devices. Does nothing if the id
    /// is unknown.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Every active entry converted to what it is worth per month and summed, so a weekly
    /// payment and a yearly one add up to a figure that means something.
    /// </summary>
    Task<decimal> GetMonthlyEquivalentTotalAsync(CancellationToken ct = default);
}
