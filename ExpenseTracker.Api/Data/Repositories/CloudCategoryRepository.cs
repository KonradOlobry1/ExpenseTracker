using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data.Repositories;

/// <summary>
/// Categories for the signed-in account, read straight from the cloud database.
/// </summary>
/// <remarks>
/// ApiDbContext has no soft-delete query filter — pull must be able to return tombstones —
/// so every read here filters <c>IsDeleted</c> explicitly. Forgetting that would surface
/// deleted rows in the UI.
///
/// Uses a context factory because Blazor Server can render concurrent queries; see
/// CloudExpenseRepository.
/// </remarks>
public class CloudCategoryRepository(IDbContextFactory<ApiDbContext> factory, ICurrentUser user)
    : ICategoryRepository
{
    private IQueryable<Category> Mine(ApiDbContext db) =>
        db.Categories.Where(c => c.UserId == user.UserId && !c.IsDeleted);

    public async Task<List<Category>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await Mine(db).OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<Category> CreateAsync(Category category, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        category.UserId = user.UserId;
        category.CreatedAt = DateTime.UtcNow;
        category.ClientUpdatedAt = category.CreatedAt;
        db.Categories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task<Category> UpdateAsync(Category category, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        category.UserId = user.UserId;
        category.UpdatedAt = DateTime.UtcNow;
        category.ClientUpdatedAt = category.UpdatedAt.Value;
        db.Categories.Update(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var category = await Mine(db).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (category is null) return;
        if (category.IsSystem) throw new InvalidOperationException("Cannot delete a system category.");

        category.IsDeleted = true;
        category.UpdatedAt = DateTime.UtcNow;
        category.ClientUpdatedAt = category.UpdatedAt.Value;
        await db.SaveChangesAsync(ct);
    }
}
