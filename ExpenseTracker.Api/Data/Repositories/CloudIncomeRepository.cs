using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data.Repositories;

/// <summary>Income for the signed-in account. See CloudCategoryRepository on IsDeleted.</summary>
public class CloudIncomeRepository(IDbContextFactory<ApiDbContext> factory, ICurrentUser user)
    : IIncomeRepository
{
    private IQueryable<Income> Mine(ApiDbContext db) =>
        db.Incomes.Where(i => i.UserId == user.UserId && !i.IsDeleted);

    public async Task<List<Income>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Mine(db);
        if (activeOnly) query = query.Where(i => i.IsActive);
        return await query.OrderByDescending(i => i.StartDate).ToListAsync(ct);
    }

    public Task<List<Income>> GetActiveAsync(CancellationToken ct = default)
        => GetAllAsync(activeOnly: true, ct);

    public async Task<Income> CreateAsync(Income income, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        income.UserId = user.UserId;
        income.CreatedAt = DateTime.UtcNow;
        income.ClientUpdatedAt = income.CreatedAt;
        db.Incomes.Add(income);
        await db.SaveChangesAsync(ct);
        return income;
    }

    public async Task<Income> UpdateAsync(Income income, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        income.UserId = user.UserId;
        income.UpdatedAt = DateTime.UtcNow;
        income.ClientUpdatedAt = income.UpdatedAt.Value;
        db.Incomes.Update(income);
        await db.SaveChangesAsync(ct);
        return income;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var income = await Mine(db).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (income is null) return;

        income.IsDeleted = true;
        income.UpdatedAt = DateTime.UtcNow;
        income.ClientUpdatedAt = income.UpdatedAt.Value;
        await db.SaveChangesAsync(ct);
    }
}
