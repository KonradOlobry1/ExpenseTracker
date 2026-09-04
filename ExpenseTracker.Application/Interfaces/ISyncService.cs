namespace ExpenseTracker.Application.Interfaces;

public interface ISyncService
{
    Task<SyncResult> SyncAsync(CancellationToken ct = default);
    DateTime? LastSyncTime { get; }
    bool IsSyncing { get; }

    /// <summary>
    /// Whether this deployment syncs at all.
    /// </summary>
    /// <remarks>
    /// False on the web, which reads and writes the cloud database directly: there is no local
    /// replica to reconcile, so <see cref="SyncAsync"/> has nothing to do and
    /// <see cref="LastSyncTime"/> is permanently null. The UI has to ask, because a sync
    /// button that can never do anything is indistinguishable from a broken one — which is
    /// how it was reported.
    /// </remarks>
    bool IsSupported { get; }

    event Action? SyncStateChanged;
}
