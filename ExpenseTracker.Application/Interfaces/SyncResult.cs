namespace ExpenseTracker.Application.Interfaces;

/// <summary>Why <see cref="SyncResult.Succeeded"/> is false. See <see cref="AuthFailureReason"/>.</summary>
public enum SyncFailureReason
{
    NotSignedIn,
    SessionExpired,
    NetworkError,
    ServerError,
    LocalDatabaseError,
}

/// <summary>Replaces a bare <c>bool</c> on <see cref="ISyncService.SyncAsync"/>. See <see cref="AuthResult"/>.</summary>
public readonly record struct SyncResult(bool Succeeded, SyncFailureReason? Failure = null)
{
    public static SyncResult Success() => new(true);
    public static SyncResult Fail(SyncFailureReason reason) => new(false, reason);
}
