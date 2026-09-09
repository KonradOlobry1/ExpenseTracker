using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces.Repositories;

/// <summary>One payment a subscription will take, on a date, for an amount.</summary>
/// <remarks>
/// The amount is the subscription's own charge rather than a monthly equivalent: this
/// describes a real payment that will actually leave the account on that date.
/// </remarks>
public record SubscriptionOccurrence(Subscription Subscription, DateTime DueDate, decimal Amount);

/// <summary>Subscription storage. See <see cref="ICategoryRepository"/> on the two implementations.</summary>
public interface ISubscriptionRepository
{
    /// <summary>Subscriptions, name-ordered. Excludes soft-deleted ones.</summary>
    /// <param name="activeOnly">Defaults to true: a cancelled plan is not a current cost.</param>
    Task<List<Subscription>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default);

    Task<Subscription> CreateAsync(Subscription subscription, CancellationToken ct = default);

    Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken ct = default);

    /// <summary>Soft-deletes the subscription. Does nothing if the id is unknown.</summary>
    Task DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Only subscriptions still being charged — what the forecasts are built from.</summary>
    Task<List<Subscription>> GetActiveAsync(CancellationToken ct = default);
}
