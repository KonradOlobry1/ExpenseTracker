namespace ExpenseTracker.Application.Interfaces;

public interface ISyncService
{
    Task<SyncResult> SyncAsync(CancellationToken ct = default);
    DateTime? LastSyncTime { get; }
    bool IsSyncing { get; }
    event Action? SyncStateChanged;
}
