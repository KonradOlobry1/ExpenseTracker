using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces.Repositories;

public record SubscriptionOccurrence(Subscription Subscription, DateTime DueDate, decimal Amount);

public interface ISubscriptionRepository
{
    Task<List<Subscription>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default);
    Task<Subscription> CreateAsync(Subscription subscription, CancellationToken ct = default);
    Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<List<Subscription>> GetActiveAsync(CancellationToken ct = default);
}
