using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data.Repositories;

/// <summary>Expenses for the signed-in account. See CloudCategoryRepository on IsDeleted.</summary>
/// <remarks>
/// Takes a context factory rather than a scoped DbContext: Blazor Server renders can run
/// several queries at once (Analytics fires twelve, one per month) and a single context
/// throws on concurrent use. Same reason the device-side repositories use a factory.
/// </remarks>
public class CloudExpenseRepository(IDbContextFactory<ApiDbContext> factory, ICurrentUser user)
    : IExpenseRepository
{
    private IQueryable<Expense> Mine(ApiDbContext db) =>
        db.Expenses.Include(e => e.Category)
                   .Where(e => e.UserId == user.UserId && !e.IsDeleted && !e.Category.IsDeleted);

    public async Task<List<Expense>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Mine(db).OrderByDescending(e => e.Date).ToListAsync(ct);
    }

    public async Task<List<Expense>> GetByMonthAsync(int year, int month, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Mine(db)
            .Where(e => e.Date.Year == year && e.Date.Month == month)
            .OrderByDescending(e => e.Date)
            .ToListAsync(ct);
    }

    public async Task<PagedResult<Expense>> GetPagedAsync(
        ExpenseFilter filter, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Filtered(db, filter);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(e => e.Date)
            .Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<Expense>(items, total);
    }

    public async Task<decimal> GetFilteredSumAsync(ExpenseFilter filter, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Filtered(db, filter).SumAsync(e => e.Amount, ct);
    }

    private IQueryable<Expense> Filtered(ApiDbContext db, ExpenseFilter filter)
    {
        var query = Mine(db);

        if (filter.From.HasValue) query = query.Where(e => e.Date >= filter.From.Value);
        if (filter.To.HasValue) query = query.Where(e => e.Date <= filter.To.Value);
        if (filter.CategoryId.HasValue) query = query.Where(e => e.CategoryId == filter.CategoryId.Value);
        if (filter.MinAmount.HasValue) query = query.Where(e => e.Amount >= filter.MinAmount.Value);
        if (filter.MaxAmount.HasValue) query = query.Where(e => e.Amount <= filter.MaxAmount.Value);
        if (!string.IsNullOrWhiteSpace(filter.SearchText))
            query = query.Where(e => e.Description.Contains(filter.SearchText) ||
                                     (e.Notes != null && e.Notes.Contains(filter.SearchText)));

        return query;
    }

    public async Task<Expense> CreateAsync(Expense expense, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        expense.UserId = user.UserId;
        expense.CreatedAt = DateTime.UtcNow;
        expense.ClientUpdatedAt = expense.CreatedAt;
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return expense;
    }

    public async Task<Expense> UpdateAsync(Expense expense, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        expense.UserId = user.UserId;
        expense.UpdatedAt = DateTime.UtcNow;
        expense.ClientUpdatedAt = expense.UpdatedAt.Value;
        db.Expenses.Update(expense);
        await db.SaveChangesAsync(ct);
        return expense;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var expense = await Mine(db).FirstOrDefaultAsync(e => e.Id == id, ct);
        if (expense is null) return;

        expense.IsDeleted = true;
        expense.UpdatedAt = DateTime.UtcNow;
        expense.ClientUpdatedAt = expense.UpdatedAt.Value;
        await db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<int, decimal>> GetMonthlyTotalsAsync(int year, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var expenses = await Mine(db).Where(e => e.Date.Year == year).ToListAsync(ct);

        return expenses.GroupBy(e => e.Date.Month)
                       .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
    }

    public async Task<Dictionary<string, decimal>> GetCategoryTotalsAsync(
        int year, int month, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var expenses = await Mine(db)
            .Where(e => e.Date.Year == year && e.Date.Month == month)
            .ToListAsync(ct);

        return expenses.Where(e => e.Category is not null)
                       .GroupBy(e => e.Category.Name)
                       .ToDictionary(g => g.Key, g => g.Sum(e => e.Amount));
    }
}
