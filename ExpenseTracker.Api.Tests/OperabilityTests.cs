using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Contracts;
using Microsoft.AspNetCore.Hosting;
using static ExpenseTracker.Api.Tests.TestClient;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// The endpoints and limits the app needs in order to be operable rather than merely correct.
/// </summary>
public class HealthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Liveness_is_anonymous()
    {
        // App Service polls this without credentials. A 401 here reads as a dead app.
        var response = await factory.CreateClient().GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_reports_the_database()
    {
        var response = await factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}

/// <summary>
/// Sync is partitioned by account, not by address: a household behind one router is several
/// clients, and a phone moving between wifi and mobile data is one.
/// </summary>
public class SyncRateLimitFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:SyncPermitPerMinute", "5");
    }
}

public class SyncRateLimitTests(SyncRateLimitFactory factory) : IClassFixture<SyncRateLimitFactory>
{
    [Fact]
    public async Task A_burst_of_syncs_is_throttled()
    {
        var client = await factory.RegisterAsync();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
        {
            var response = await client.PostAsJsonAsync("/api/sync/push", new SyncPushRequest());
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task One_account_cannot_exhaust_anothers_budget()
    {
        // The whole point of partitioning by account. Both clients come from the same loopback
        // address, so an IP-partitioned limiter would fail this.
        var alice = await factory.RegisterAsync();
        var bob = await factory.RegisterAsync();

        for (var i = 0; i < 8; i++)
            await alice.PostAsJsonAsync("/api/sync/push", new SyncPushRequest());

        var bobsFirstSync = await bob.PostAsJsonAsync("/api/sync/push", new SyncPushRequest());

        Assert.Equal(HttpStatusCode.OK, bobsFirstSync.StatusCode);
    }
}
