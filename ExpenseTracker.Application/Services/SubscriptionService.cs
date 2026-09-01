using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using ExpenseTracker.Domain.Services;

namespace ExpenseTracker.Application.Services;

public class SubscriptionService(ISubscriptionRepository repository) : ISubscriptionService
{
    public Task<List<Subscription>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default)
        => repository.GetAllAsync(activeOnly, ct);

    public Task<Subscription> CreateAsync(Subscription subscription, CancellationToken ct = default)
        => repository.CreateAsync(subscription, ct);

    public Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken ct = default)
        => repository.UpdateAsync(subscription, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => repository.DeleteAsync(id, ct);

    public async Task<decimal> GetMonthlyEquivalentTotalAsync(CancellationToken ct = default)
    {
        var subs = await repository.GetActiveAsync(ct);
        return subs.Sum(s => PredictionEngine.ToMonthlyEquivalent(s.Amount, s.BillingCycle));
    }

    public List<SubscriptionOccurrence> GetUpcomingOccurrences(Subscription subscription, int cycles)
    {
        var dates = PredictionEngine.ForecastOccurrences(
            subscription.StartDate, subscription.BillingCycle, DateTime.Today, cycles);
        return dates.Select(d => new SubscriptionOccurrence(subscription, d, subscription.Amount)).ToList();
    }

    public List<SubscriptionOccurrence> GetAllUpcomingOccurrences(List<Subscription> subscriptions, DateTime until)
    {
        return subscriptions
            .Where(s => s.IsActive && (s.EndDate == null || s.EndDate >= DateTime.Today))
            .SelectMany(s =>
            {
                var dates = PredictionEngine.OccurrencesInRange(s.StartDate, s.BillingCycle, DateTime.Today, until);
                return dates.Select(d => new SubscriptionOccurrence(s, d, s.Amount));
            })
            .OrderBy(o => o.DueDate)
            .ToList();
    }
}
