namespace ExpenseTracker.Domain.Entities;

public class Expense : ISyncEntity
{
    public int Id { get; set; }
    public Guid SyncId { get; set; } = Guid.NewGuid();
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime Date { get; set; } = DateTime.Today;
    public int CategoryId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Tombstone. Deletes are soft so they can propagate to other devices.</summary>
    public bool IsDeleted { get; set; }

    // ── Sync protocol (see ISyncEntity) ──────────────────────────────────────
    /// <summary>Owning account. Mapped in the cloud database only.</summary>
    public string? UserId { get; set; }

    /// <summary>Client edit time, for conflict resolution. Cloud only.</summary>
    public DateTime ClientUpdatedAt { get; set; }

    public Category Category { get; set; } = null!;
}
