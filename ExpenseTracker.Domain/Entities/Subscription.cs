using System.ComponentModel.DataAnnotations.Schema;
using ExpenseTracker.Domain.Services;

namespace ExpenseTracker.Domain.Entities;

public enum BillingCycle { Weekly, Monthly, Quarterly, Yearly }

public class Subscription : ISyncEntity
{
    public int Id { get; set; }
    public Guid SyncId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public BillingCycle BillingCycle { get; set; } = BillingCycle.Monthly;
    public DateTime StartDate { get; set; } = DateTime.Today;
    public DateTime? EndDate { get; set; }
    public int CategoryId { get; set; }
    public string? Notes { get; set; }
    public string? Url { get; set; }
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

    public Category Category { get; set; } = null!;

    [NotMapped]
    public DateTime? NextPaymentDate =>
        PredictionEngine.NextOccurrence(StartDate, BillingCycle, DateTime.Today);
}
