using ExpenseTracker.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// Boots the real API against a private in-memory SQLite database.
/// </summary>
/// <remarks>
/// A shared-cache in-memory database lives only as long as a connection to it is open, so the
/// factory holds one open for its lifetime. Each factory instance gets its own database, which
/// keeps test classes isolated.
///
/// The schema comes from EnsureCreated, not the migrations: the API targets SQL Server and
/// those migrations cannot be replayed against SQLite. So these tests verify the *model*, not
/// the migration scripts — nothing here would catch a migration that fails to apply. Closing
/// that gap needs LocalDB or Testcontainers; worth doing if this stops being a personal app.
/// </remarks>
public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection =
        new($"Data Source=api-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();

        builder.UseSetting("RunMigrationsAtStartup", "false");

        // Every test request comes from the same loopback address, so they all land in one
        // rate-limit partition. Raise the ceiling so ordinary tests are unaffected; the
        // rate-limiting tests lower it deliberately.
        builder.UseSetting("RateLimit:AuthPermitPerMinute", "10000");
        builder.UseSetting("Jwt:Key", "test-signing-key-that-is-comfortably-over-32-bytes-long");
        builder.UseSetting("Jwt:Issuer", "ExpenseTracker");
        builder.UseSetting("Jwt:Audience", "ExpenseTrackerApp");

        builder.ConfigureServices(services =>
        {
            // AddDbContext registers the options *and* the provider's own services. Dropping
            // only DbContextOptions<T> leaves SqlServer's provider behind, and EF refuses to
            // run with two providers in one container — so clear everything EF registered for
            // this context before adding SQLite.
            var efDescriptors = services
                .Where(d => d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true
                         || d.ServiceType == typeof(ApiDbContext))
                .ToList();
            foreach (var descriptor in efDescriptors)
                services.Remove(descriptor);

            // Mirror the app's registration: a factory for the repositories and a scoped
            // instance for the controllers.
            services.AddDbContextFactory<ApiDbContext>(o => o.UseSqlite(_connection));
            services.AddScoped<ApiDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<ApiDbContext>>().CreateDbContext());
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ApiDbContext>().Database.EnsureCreated();

        return host;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
