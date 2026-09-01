namespace ExpenseTracker.Domain.Entities;

public class Category : ISyncEntity
{
    public int Id { get; set; }
    public Guid SyncId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "attach_money";
    public string Color { get; set; } = "#607D8B";
    public bool IsSystem { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Tombstone. Deletes are soft so they can propagate to other devices.</summary>
    public bool IsDeleted { get; set; }


    // ── Sync protocol (see ISyncEntity) ──────────────────────────────────────
    /// <summary>Owning account. Mapped in the cloud database only.</summary>
    public string? UserId { get; set; }

    /// <summary>Client edit time, for conflict resolution. Cloud only.</summary>
    public DateTime ClientUpdatedAt { get; set; }

    public ICollection<Expense> Expenses { get; set; } = [];
    public ICollection<Subscription> Subscriptions { get; set; } = [];
}
