using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Interfaces.Repositories;

public interface IIncomeRepository
{
    Task<List<Income>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default);
    Task<Income> CreateAsync(Income income, CancellationToken ct = default);
    Task<Income> UpdateAsync(Income income, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<List<Income>> GetActiveAsync(CancellationToken ct = default);
}
