using ExpenseTracker.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Persistence;

/// <summary>
/// Applies the device's SQLite migrations, once per app launch.
/// </summary>
/// <remarks>
/// Registered as a singleton, because the whole point is that the work happens once no matter
/// how many callers ask. The first call starts the migration and every later one — including
/// calls that arrive while it is still running — waits on that same task rather than starting
/// a second migration against the same file.
///
/// A failure is remembered. If the migration throws, every later call sees the same failed task
/// instead of retrying: a database that could not be migrated will not migrate any better a
/// second later, and retrying in a loop behind a spinner would just hide the problem.
/// </remarks>
public class DatabaseInitializer(
    IDbContextFactory<AppDbContext> factory,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    private readonly object _sync = new();
    private Task? _run;

    public Task EnsureReadyAsync(CancellationToken ct = default)
    {
        Task run;
        lock (_sync)
            run = _run ??= MigrateAsync();

        // The caller's token stops it waiting; it does not travel into the migration. A caller
        // giving up must not leave the schema half-applied for everyone else.
        return run.WaitAsync(ct);
    }

    private async Task MigrateAsync()
    {
        // Off the caller's context on purpose. The first caller is the UI, and the migration
        // has no need of the UI thread.
        await Task.Run(async () =>
        {
            await using var db = await factory.CreateDbContextAsync();

            try
            {
                var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();

                logger.LogInformation("Migrations applied: {Applied}", string.Join(", ", applied));
                logger.LogInformation("Migrations pending: {Pending}",
                    pending.Count == 0 ? "(none)" : string.Join(", ", pending));

                if (pending.Count == 0) return;

                await db.Database.MigrateAsync();

                var still = (await db.Database.GetPendingMigrationsAsync()).ToList();
                if (still.Count > 0)
                    logger.LogError("Migrations still pending after Migrate: {Pending}",
                        string.Join(", ", still));
            }
            catch (Exception ex)
            {
                // A failed migration leaves the database in an unknown shape. Surfaced rather
                // than swallowed: running the app against a half-migrated schema produces
                // failures far from the cause.
                logger.LogError(ex, "Database migration failed.");
                throw;
            }
        });
    }
}
