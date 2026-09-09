namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// Brings the local database up to the schema this build expects, before anything reads it.
/// </summary>
/// <remarks>
/// A device carries its replica across app updates, so a build that adds a migration opens on a
/// database one version behind. Applying migrations used to happen in MauiProgram, blocking the
/// launch thread on <c>GetAwaiter().GetResult()</c> — which on a cold first install, with every
/// migration to replay, is exactly the shape of an Android ANR.
///
/// Behind an interface because the two heads differ: the device migrates its own SQLite file,
/// while the API applies its SQL Server migrations at startup long before any page renders, so
/// the web implementation has nothing to do. Same capability-flag reasoning as
/// <see cref="ISyncService.IsSupported"/> — a difference between deployments, expressed once,
/// rather than a branch in the UI.
///
/// Implementations must be safe to call repeatedly and from several callers at once, and must
/// run the migration only the first time.
/// </remarks>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Completes once the database is ready to query. Cheap on every call after the first.
    /// </summary>
    /// <param name="ct">
    /// Stops this caller waiting. It does not cancel the migration itself — abandoning one
    /// half-applied would leave the replica in a shape nothing else expects.
    /// </param>
    Task EnsureReadyAsync(CancellationToken ct = default);
}
