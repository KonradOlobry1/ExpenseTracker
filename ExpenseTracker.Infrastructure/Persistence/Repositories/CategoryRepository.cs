using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence.Repositories;

public class CategoryRepository(IDbContextFactory<AppDbContext> factory) : ICategoryRepository
{
    public async Task<List<Category>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Categories.OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<Category> CreateAsync(Category category, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task<Category> UpdateAsync(Category category, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Categories.Update(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var category = await db.Categories.FindAsync([id], ct);
        if (category is null) return;
        if (category.IsSystem) throw new InvalidOperationException("Cannot delete a system category.");

        // Soft delete: a hard delete cannot be communicated to other devices, which would
        // simply re-send the row on their next push.
        category.IsDeleted = true;
        category.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
