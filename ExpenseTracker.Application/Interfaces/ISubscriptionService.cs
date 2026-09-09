using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;

namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// Recurring costs for the current account, and the forecasting built on them.
/// </summary>
/// <remarks>
/// A subscription is a cost plus a cycle. Almost everything useful here — comparing one plan
/// against another, totalling them, drawing a timeline — means resolving that cycle first,
/// which is what <c>PredictionEngine</c> does and why these methods return normalised figures
/// rather than the raw amount.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>
    /// Subscriptions, name-ordered. Excludes soft-deleted ones.
    /// </summary>
    /// <param name="activeOnly">
    /// Defaults to <c>true</c>, unlike <see cref="IIncomeService.GetAllAsync"/>: a cancelled
    /// subscription is not something the user is still paying for, so it would only inflate
    /// the list and every total drawn from it.
    /// </param>
    Task<List<Subscription>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default);

    Task<Subscription> CreateAsync(Subscription subscription, CancellationToken ct = default);

    Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes the subscription, leaving a tombstone for other devices. Does nothing if
    /// the id is unknown.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Every active subscription converted to what it costs per month and summed — the figure
    /// that makes a yearly plan and a weekly one comparable.
    /// </summary>
    Task<decimal> GetMonthlyEquivalentTotalAsync(CancellationToken ct = default);

    /// <summary>
    /// The next <paramref name="cycles"/> payment dates for one subscription, starting from
    /// its next due date.
    /// </summary>
    List<SubscriptionOccurrence> GetUpcomingOccurrences(Subscription subscription, int cycles);

    /// <summary>
    /// Every payment due across all the given subscriptions up to <paramref name="until"/>,
    /// in date order — the timeline on the subscriptions page.
    /// </summary>
    /// <remarks>
    /// Bounded by a date rather than a count, because the useful question is "what is due
    /// before the end of the quarter", and a weekly plan and a yearly one produce wildly
    /// different numbers of occurrences over the same span.
    /// </remarks>
    List<SubscriptionOccurrence> GetAllUpcomingOccurrences(List<Subscription> subscriptions, DateTime until);
}
