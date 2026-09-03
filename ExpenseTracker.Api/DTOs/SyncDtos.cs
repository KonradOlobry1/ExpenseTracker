using ExpenseTracker.Contracts;
using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Api.DTOs;

// The wire records themselves live in ExpenseTracker.Contracts, shared with the device client
// and the tests. What stays here is the mapping between them and the storage entities, which
// is the server's business and nobody else's.

public static class SyncMapping
{
    /// <summary>The client's own edit time — its UpdatedAt, or CreatedAt if never edited.</summary>
    public static DateTime ClientStamp(DateTime createdAt, DateTime? updatedAt) => updatedAt ?? createdAt;

    // Expenses and subscriptions reference their category by SyncId on the wire and by the
    // cloud database's own integer key in storage, so these take the resolved id.
    public static Expense ToEntity(this SyncExpenseDto d, string userId, int categoryId) => new()
    {
        SyncId = d.SyncId, UserId = userId, Description = d.Description, Amount = d.Amount,
        Date = d.Date, CategoryId = categoryId, Notes = d.Notes,
        CreatedAt = d.CreatedAt, IsDeleted = d.IsDeleted,
        ClientUpdatedAt = ClientStamp(d.CreatedAt, d.UpdatedAt)
    };

    public static void Apply(this SyncExpenseDto d, Expense e, int categoryId)
    {
        e.Description = d.Description; e.Amount = d.Amount; e.Date = d.Date;
        e.CategoryId = categoryId; e.Notes = d.Notes; e.IsDeleted = d.IsDeleted;
        e.ClientUpdatedAt = ClientStamp(d.CreatedAt, d.UpdatedAt);
    }

    public static SyncExpenseDto ToDto(this Expense e, Guid categorySyncId) => new(
        e.SyncId, e.Description, e.Amount, e.Date, categorySyncId, e.Notes,
        e.CreatedAt, e.UpdatedAt, e.IsDeleted);

    public static Income ToEntity(this SyncIncomeDto d, string userId) => new()
    {
        SyncId = d.SyncId, UserId = userId, Description = d.Description, Amount = d.Amount,
        BillingCycle = ParseCycle(d.BillingCycle), StartDate = d.StartDate, Notes = d.Notes,
        IsActive = d.IsActive, CreatedAt = d.CreatedAt, IsDeleted = d.IsDeleted,
        ClientUpdatedAt = ClientStamp(d.CreatedAt, d.UpdatedAt)
    };

    public static void Apply(this SyncIncomeDto d, Income i)
    {
        i.Description = d.Description; i.Amount = d.Amount;
        i.BillingCycle = ParseCycle(d.BillingCycle); i.StartDate = d.StartDate;
        i.Notes = d.Notes; i.IsActive = d.IsActive; i.IsDeleted = d.IsDeleted;
        i.ClientUpdatedAt = ClientStamp(d.CreatedAt, d.UpdatedAt);
    }

    public static SyncIncomeDto ToDto(this Income i) => new(
        i.SyncId, i.Description, i.Amount, i.BillingCycle.ToString(), i.StartDate,
        i.Notes, i.IsActive, i.CreatedAt, i.UpdatedAt, i.IsDeleted);

    public static Subscription ToEntity(this SyncSubscriptionDto d, string userId, int categoryId) => new()
    {
        SyncId = d.SyncId, UserId = userId, Name = d.Name, Amount = d.Amount,
        BillingCycle = ParseCycle(d.BillingCycle), StartDate = d.StartDate, EndDate = d.EndDate,
        CategoryId = categoryId, Notes = d.Notes, Url = d.Url, IsActive = d.IsActive,
        CreatedAt = d.CreatedAt, IsDeleted = d.IsDeleted,
        ClientUpdatedAt = ClientStamp(d.CreatedAt, d.UpdatedAt)
    };

    public static void Apply(this SyncSubscriptionDto d, Subscription s, int categoryId)
    {
        s.Name = d.Name; s.Amount = d.Amount; s.BillingCycle = ParseCycle(d.BillingCycle);
        s.StartDate = d.StartDate; s.EndDate = d.EndDate; s.CategoryId = categoryId;
        s.Notes = d.Notes; s.Url = d.Url; s.IsActive = d.IsActive; s.IsDeleted = d.IsDeleted;
        s.ClientUpdatedAt = ClientStamp(d.CreatedAt, d.UpdatedAt);
    }

    public static SyncSubscriptionDto ToDto(this Subscription s, Guid categorySyncId) => new(
        s.SyncId, s.Name, s.Amount, s.BillingCycle.ToString(), s.StartDate, s.EndDate,
        categorySyncId, s.Notes, s.Url, s.IsActive, s.CreatedAt, s.UpdatedAt, s.IsDeleted);

    public static Category ToEntity(this SyncCategoryDto d, string userId) => new()
    {
        SyncId = d.SyncId, UserId = userId, Name = d.Name, Icon = d.Icon, Color = d.Color,
        IsSystem = d.IsSystem, CreatedAt = d.CreatedAt, IsDeleted = d.IsDeleted,
        ClientUpdatedAt = ClientStamp(d.CreatedAt, d.UpdatedAt)
    };

    public static void Apply(this SyncCategoryDto d, Category c)
    {
        c.Name = d.Name; c.Icon = d.Icon; c.Color = d.Color; c.IsSystem = d.IsSystem;
        c.IsDeleted = d.IsDeleted;
        c.ClientUpdatedAt = ClientStamp(d.CreatedAt, d.UpdatedAt);
    }

    public static SyncCategoryDto ToDto(this Category c) => new(
        c.SyncId, c.Name, c.Icon, c.Color, c.IsSystem, c.CreatedAt, c.UpdatedAt, c.IsDeleted);

    private static BillingCycle ParseCycle(string value)
        => Enum.TryParse<BillingCycle>(value, out var cycle) ? cycle : BillingCycle.Monthly;
}
