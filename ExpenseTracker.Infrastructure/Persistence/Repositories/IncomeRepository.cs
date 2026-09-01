using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence.Repositories;

public class IncomeRepository(IDbContextFactory<AppDbContext> factory) : IIncomeRepository
{
    public async Task<List<Income>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Incomes.AsQueryable();
        if (activeOnly)
            query = query.Where(i => i.IsActive);
        return await query.OrderByDescending(i => i.StartDate).ToListAsync(ct);
    }

    public async Task<Income> CreateAsync(Income income, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        income.CreatedAt = DateTime.UtcNow;
        db.Incomes.Add(income);
        await db.SaveChangesAsync(ct);
        return income;
    }

    public async Task<Income> UpdateAsync(Income income, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Incomes.Update(income);
        await db.SaveChangesAsync(ct);
        return income;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var income = await db.Incomes.FindAsync([id], ct);
        if (income is null) return;
        income.IsDeleted = true;
        income.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Income>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Incomes.Where(i => i.IsActive).ToListAsync(ct);
    }
}
