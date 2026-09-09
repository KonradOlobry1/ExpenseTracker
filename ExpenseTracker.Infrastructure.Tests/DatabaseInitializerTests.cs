using ExpenseTracker.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExpenseTracker.Infrastructure.Tests;

/// <summary>
/// Applying the device's migrations at launch.
/// </summary>
/// <remarks>
/// This used to be a <c>GetAwaiter().GetResult()</c> in MauiProgram, holding the launch thread
/// until every pending migration had replayed — on a cold install, the whole history, with
/// Android's ANR watchdog running. It now happens behind the spinner MainLayout already shows.
///
/// What moved with it is the risk: the migration is no longer serialised by being the only
/// thing running. Several callers can now arrive at once, so "runs exactly once" is a property
/// this has to hold rather than get for free, and that is most of what these test.
/// </remarks>
public class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IDbContextFactory<AppDbContext> _factory;

    public DatabaseInitializerTests()
    {
        // A real file-backed schema would do too, but a shared-cache in-memory database keeps
        // the test self-cleaning; it lives exactly as long as this connection.
        _connection = new SqliteConnection($"Data Source=init-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();

        _factory = new TestDbContextFactory(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
    }

    private DatabaseInitializer New() =>
        new(_factory, NullLogger<DatabaseInitializer>.Instance);

    [Fact]
    public async Task It_creates_a_schema_the_app_can_query()
    {
        // Unmigrated, this throws "no such table". The assertion is that ordinary data access
        // works afterwards, not that some migration-history row exists.
        var initializer = New();

        await initializer.EnsureReadyAsync();

        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.Expenses.ToListAsync());
    }

    [Fact]
    public async Task It_applies_every_migration()
    {
        var initializer = New();

        await initializer.EnsureReadyAsync();

        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Calling_it_again_is_harmless()
    {
        // Every navigation re-runs the layout's auth check; the initializer must not care.
        var initializer = New();

        await initializer.EnsureReadyAsync();
        await initializer.EnsureReadyAsync();
        await initializer.EnsureReadyAsync();

        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Callers_that_arrive_together_share_one_migration()
    {
        // The property the old blocking call got for free. Two concurrent MigrateAsync runs
        // against one SQLite file race on the migration-history table, and the loser throws.
        var initializer = New();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => initializer.EnsureReadyAsync()));

        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task A_caller_giving_up_does_not_abandon_the_migration()
    {
        // The token stops this caller waiting, not the schema change. Cancelling a migration
        // part-applied would leave the replica in a shape nothing else expects.
        var initializer = New();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => initializer.EnsureReadyAsync(cancelled.Token));

        // The run it started still finishes, and the next caller sees a ready database.
        await initializer.EnsureReadyAsync();

        await using var db = _factory.CreateDbContext();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    public void Dispose() => _connection.Dispose();
}
