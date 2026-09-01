using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;

namespace ExpenseTracker.Application.Interfaces;

public interface ISubscriptionService
{
    Task<List<Subscription>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default);
    Task<Subscription> CreateAsync(Subscription subscription, CancellationToken ct = default);
    Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<decimal> GetMonthlyEquivalentTotalAsync(CancellationToken ct = default);
    List<SubscriptionOccurrence> GetUpcomingOccurrences(Subscription subscription, int cycles);
    List<SubscriptionOccurrence> GetAllUpcomingOccurrences(List<Subscription> subscriptions, DateTime until);
}
