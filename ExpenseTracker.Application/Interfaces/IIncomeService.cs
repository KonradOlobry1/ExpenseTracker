using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Application.Interfaces;

public interface IIncomeService
{
    Task<List<Income>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default);
    Task<Income> CreateAsync(Income income, CancellationToken ct = default);
    Task<Income> UpdateAsync(Income income, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<decimal> GetMonthlyEquivalentTotalAsync(CancellationToken ct = default);
}
