using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Api.Data.Repositories;

/// <summary>Subscriptions for the signed-in account. See CloudCategoryRepository on IsDeleted.</summary>
public class CloudSubscriptionRepository(IDbContextFactory<ApiDbContext> factory, ICurrentUser user)
    : ISubscriptionRepository
{
    private IQueryable<Subscription> Mine(ApiDbContext db) =>
        db.Subscriptions.Include(s => s.Category)
                        .Where(s => s.UserId == user.UserId && !s.IsDeleted && !s.Category.IsDeleted);

    public async Task<List<Subscription>> GetAllAsync(bool activeOnly = false, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = Mine(db);
        if (activeOnly) query = query.Where(s => s.IsActive);
        return await query.OrderBy(s => s.Name).ToListAsync(ct);
    }

    public Task<List<Subscription>> GetActiveAsync(CancellationToken ct = default)
        => GetAllAsync(activeOnly: true, ct);

    public async Task<Subscription> CreateAsync(Subscription subscription, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        subscription.UserId = user.UserId;
        subscription.CreatedAt = DateTime.UtcNow;
        subscription.ClientUpdatedAt = subscription.CreatedAt;
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);
        return subscription;
    }

    public async Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        subscription.UserId = user.UserId;
        subscription.UpdatedAt = DateTime.UtcNow;
        subscription.ClientUpdatedAt = subscription.UpdatedAt.Value;
        db.Subscriptions.Update(subscription);
        await db.SaveChangesAsync(ct);
        return subscription;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var sub = await Mine(db).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (sub is null) return;

        sub.IsDeleted = true;
        sub.UpdatedAt = DateTime.UtcNow;
        sub.ClientUpdatedAt = sub.UpdatedAt.Value;
        await db.SaveChangesAsync(ct);
    }
}
