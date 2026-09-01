using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;

namespace ExpenseTracker.Application.Interfaces;

public interface IExpenseService
{
    Task<List<Expense>> GetAllAsync(CancellationToken ct = default);
    Task<List<Expense>> GetByMonthAsync(int year, int month, CancellationToken ct = default);
    Task<Expense> CreateAsync(Expense expense, CancellationToken ct = default);
    Task<Expense> UpdateAsync(Expense expense, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
    Task<Dictionary<int, decimal>> GetMonthlyTotalsAsync(int year, CancellationToken ct = default);
    Task<Dictionary<string, decimal>> GetCategoryTotalsAsync(int year, int month, CancellationToken ct = default);
    Task<PagedResult<Expense>> GetPagedAsync(ExpenseFilter filter, int page, int pageSize, CancellationToken ct = default);
    Task<decimal> GetFilteredSumAsync(ExpenseFilter filter, CancellationToken ct = default);
}
