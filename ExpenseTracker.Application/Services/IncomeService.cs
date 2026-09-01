using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using ExpenseTracker.Domain.Services;

namespace ExpenseTracker.Application.Services;

public class IncomeService(IIncomeRepository repository) : IIncomeService
{
    public Task<List<Income>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default)
        => repository.GetAllAsync(activeOnly, ct);

    public Task<Income> CreateAsync(Income income, CancellationToken ct = default)
        => repository.CreateAsync(income, ct);

    public Task<Income> UpdateAsync(Income income, CancellationToken ct = default)
        => repository.UpdateAsync(income, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => repository.DeleteAsync(id, ct);

    public async Task<decimal> GetMonthlyEquivalentTotalAsync(CancellationToken ct = default)
    {
        var incomes = await repository.GetActiveAsync(ct);
        return incomes.Sum(i => PredictionEngine.ToMonthlyEquivalent(i.Amount, i.BillingCycle));
    }
}
