using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence.Repositories;

public class ExpenseRepository(IDbContextFactory<AppDbContext> factory) : IExpenseRepository
{
    public async Task<List<Expense>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Expenses
            .Include(e => e.Category)
            .OrderByDescending(e => e.Date)
            .ToListAsync(ct);
    }

    public async Task<List<Expense>> GetByMonthAsync(int year, int month, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Expenses
            .Include(e => e.Category)
            .Where(e => e.Date.Year == year && e.Date.Month == month)
            .OrderByDescending(e => e.Date)
            .ToListAsync(ct);
    }

    public async Task<PagedResult<Expense>> GetPagedAsync(ExpenseFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = BuildFilteredQuery(db, filter);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.Date)
            .Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<Expense>(items, total);
    }

    public async Task<decimal> GetFilteredSumAsync(ExpenseFilter filter, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await BuildFilteredQuery(db, filter).SumAsync(e => e.Amount, ct);
    }

    private static IQueryable<Expense> BuildFilteredQuery(AppDbContext db, ExpenseFilter filter)
    {
        var query = db.Expenses.Include(e => e.Category).AsQueryable();

        if (filter.From.HasValue)
            query = query.Where(e => e.Date >= filter.From.Value);
        if (filter.To.HasValue)
            query = query.Where(e => e.Date <= filter.To.Value);
        if (filter.CategoryId.HasValue)
            query = query.Where(e => e.CategoryId == filter.CategoryId.Value);
        if (filter.MinAmount.HasValue)
            query = query.Where(e => e.Amount >= filter.MinAmount.Value);
        if (filter.MaxAmount.HasValue)
            query = query.Where(e => e.Amount <= filter.MaxAmount.Value);
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
            query = query.Where(e => e.Description.Contains(filter.SearchText) ||
                                     (e.Notes != null && e.Notes.Contains(filter.SearchText)));

        return query;
    }

    public async Task<Expense> CreateAsync(Expense expense, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        expense.CreatedAt = DateTime.UtcNow;
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return expense;
    }

    public async Task<Expense> UpdateAsync(Expense expense, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        expense.UpdatedAt = DateTime.UtcNow;
        db.Expenses.Update(expense);
        await db.SaveChangesAsync(ct);
        return expense;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var expense = await db.Expenses.FindAsync([id], ct);
        if (expense is null) return;
        expense.IsDeleted = true;
        expense.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<int, decimal>> GetMonthlyTotalsAsync(int year, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var expenses = await db.Expenses
            .Where(e => e.Date.Year == year)
            .ToListAsync(ct);

        return expenses
            .GroupBy(e => e.Date.Month)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
    }

    public async Task<Dictionary<string, decimal>> GetCategoryTotalsAsync(int year, int month, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var expenses = await db.Expenses
            .Include(e => e.Category)
            .Where(e => e.Date.Year == year && e.Date.Month == month)
            .ToListAsync(ct);

        // The Expense query filter already excludes rows whose category is soft-deleted,
        // but Category can still be null if the include is ever relaxed — group defensively
        // rather than throwing inside a dashboard query.
        return expenses
            .Where(e => e.Category is not null)
            .GroupBy(e => e.Category.Name)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
    }
}
