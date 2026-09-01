using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Infrastructure.Persistence.Repositories;

public class SubscriptionRepository(IDbContextFactory<AppDbContext> factory) : ISubscriptionRepository
{
    public async Task<List<Subscription>> GetAllAsync(bool activeOnly = true, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.Subscriptions.Include(s => s.Category).AsQueryable();
        if (activeOnly)
            query = query.Where(s => s.IsActive);
        return await query.OrderBy(s => s.Name).ToListAsync(ct);
    }

    public async Task<Subscription> CreateAsync(Subscription subscription, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        subscription.CreatedAt = DateTime.UtcNow;
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync(ct);
        return subscription;
    }

    public async Task<Subscription> UpdateAsync(Subscription subscription, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        db.Subscriptions.Update(subscription);
        await db.SaveChangesAsync(ct);
        return subscription;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var sub = await db.Subscriptions.FindAsync([id], ct);
        if (sub is null) return;
        sub.IsDeleted = true;
        sub.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<Subscription>> GetActiveAsync(CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Subscriptions.Where(s => s.IsActive).ToListAsync(ct);
    }
}
