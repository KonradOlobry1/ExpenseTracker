namespace ExpenseTracker.Application.Interfaces;

public interface ISyncService
{
    Task<bool> SyncAsync(CancellationToken ct = default);
    DateTime? LastSyncTime { get; }
    bool IsSyncing { get; }
    event Action? SyncStateChanged;
}
