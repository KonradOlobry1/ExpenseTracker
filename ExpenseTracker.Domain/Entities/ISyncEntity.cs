namespace ExpenseTracker.Domain.Entities;

/// <summary>
/// The sync-protocol fields every synchronised row carries.
/// </summary>
/// <remarks>
/// These entities are mapped by two DbContexts against two databases:
/// <list type="bullet">
/// <item>the device's SQLite replica — single user, so <see cref="UserId"/> and
/// <see cref="ClientUpdatedAt"/> are not mapped there;</item>
/// <item>the cloud SQL Server database — multi-user, and the source of truth.</item>
/// </list>
/// Each context ignores the properties it has no use for, so the entity carries the union
/// of both. That is deliberate: one definition of Expense beats two that drift apart.
/// </remarks>
public interface ISyncEntity
{
    Guid SyncId { get; set; }

    /// <summary>Owning account. Cloud only — the device replica holds a single user's data.</summary>
    string? UserId { get; set; }

    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// The client's own edit time, used to resolve conflicts. Distinct from
    /// <see cref="UpdatedAt"/>, which the server sets on receipt and which drives `since`
    /// filtering — the two come from different clocks. Cloud only.
    /// </summary>
    DateTime ClientUpdatedAt { get; set; }

    bool IsDeleted { get; set; }
}
