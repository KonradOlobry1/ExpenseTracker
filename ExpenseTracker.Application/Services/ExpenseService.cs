using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;

namespace ExpenseTracker.Application.Services;

public class ExpenseService(IExpenseRepository repository) : IExpenseService
{
    public Task<List<Expense>> GetAllAsync(CancellationToken ct = default)
        => repository.GetAllAsync(ct);

    public Task<List<Expense>> GetByMonthAsync(int year, int month, CancellationToken ct = default)
        => repository.GetByMonthAsync(year, month, ct);

    public Task<Expense> CreateAsync(Expense expense, CancellationToken ct = default)
        => repository.CreateAsync(expense, ct);

    public Task<Expense> UpdateAsync(Expense expense, CancellationToken ct = default)
        => repository.UpdateAsync(expense, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default)
        => repository.DeleteAsync(id, ct);

    public Task<Dictionary<int, decimal>> GetMonthlyTotalsAsync(int year, CancellationToken ct = default)
        => repository.GetMonthlyTotalsAsync(year, ct);

    public Task<Dictionary<string, decimal>> GetCategoryTotalsAsync(int year, int month, CancellationToken ct = default)
        => repository.GetCategoryTotalsAsync(year, month, ct);

    public Task<PagedResult<Expense>> GetPagedAsync(ExpenseFilter filter, int page, int pageSize, CancellationToken ct = default)
        => repository.GetPagedAsync(filter, page, pageSize, ct);

    public Task<decimal> GetFilteredSumAsync(ExpenseFilter filter, CancellationToken ct = default)
        => repository.GetFilteredSumAsync(filter, ct);
}
