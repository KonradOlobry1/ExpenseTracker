namespace ExpenseTracker.Domain.Entities;

public class Income : ISyncEntity
{
    public int Id { get; set; }
    public Guid SyncId { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Tombstone. Deletes are soft so they can propagate to other devices.</summary>
    public bool IsDeleted { get; set; }

    // ── Sync protocol (see ISyncEntity) ──────────────────────────────────────
    /// <summary>Owning account. Mapped in the cloud database only.</summary>
    public string? UserId { get; set; }

    /// <summary>Client edit time, for conflict resolution. Cloud only.</summary>
    public DateTime ClientUpdatedAt { get; set; }
}
