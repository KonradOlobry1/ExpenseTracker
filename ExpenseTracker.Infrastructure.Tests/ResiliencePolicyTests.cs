using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ExpenseTracker.Infrastructure.External;
using Microsoft.Extensions.Http.Resilience;

namespace ExpenseTracker.Infrastructure.Tests;

/// <summary>Fails a fixed number of times with a transient status, then succeeds.</summary>
file sealed class FlakyHandler(int failuresBeforeSuccess) : HttpMessageHandler
{
    public int AttemptCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        AttemptCount++;
        var status = AttemptCount <= failuresBeforeSuccess ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
        return Task.FromResult(new HttpResponseMessage(status));
    }
}

/// <summary>
/// MauiProgram.cs registers AuthService and SyncService via
/// <c>AddHttpClient&lt;T&gt;().AddStandardResilienceHandler()</c>, the same shape
/// <c>EnableRetryOnFailure</c> gives the EF side for the same reason: Azure SQL's serverless
/// tier auto-pauses, and the first request after idle needs a retry to survive the wake-up
/// window. That configuration is exercised nowhere else — <see cref="SyncHarness"/> gives
/// <c>SyncService</c> a raw <c>HttpClient</c> wrapping <see cref="StubApi"/> directly, bypassing
/// <c>IHttpClientFactory</c> and the resilience handler entirely, which is correct for testing
/// sync logic but proves nothing about retries. These build the identical registration against
/// a handler that fails on purpose, so what's checked is that the call was actually made and
/// actually retried — not that <c>AddStandardResilienceHandler()</c> merely compiles.
/// </summary>
public class ResiliencePolicyTests
{
    private static HttpClient BuildResilientClient(HttpMessageHandler stub)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("test")
            .ConfigurePrimaryHttpMessageHandler(() => stub)
            .AddStandardResilienceHandler(options =>
            {
                // Same handler, shorter delay — nothing about what's being tested changes,
                // only how long the test takes to run it.
                options.Retry.MaxRetryAttempts = 2;
                options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            });

        return services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient("test");
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_eventually_succeeds()
    {
        var handler = new FlakyHandler(failuresBeforeSuccess: 1);
        var client = BuildResilientClient(handler);

        var response = await client.GetAsync("https://stub.local/probe");

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal(2, handler.AttemptCount);
    }

    [Fact]
    public async Task Failures_beyond_the_retry_budget_still_fail()
    {
        var handler = new FlakyHandler(failuresBeforeSuccess: 10);
        var client = BuildResilientClient(handler);

        var response = await client.GetAsync("https://stub.local/probe");

        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(3, handler.AttemptCount);   // the original attempt plus MaxRetryAttempts
    }
}

/// <summary>
/// The timeout budget those retries run inside.
/// </summary>
/// <remarks>
/// The standard handler's defaults are 10 seconds per attempt and 30 in total, which are
/// shorter than the Azure SQL serverless resume this whole policy exists to survive — so the
/// first call after an idle period could not succeed under them however many times it retried.
/// It failed with a TimeoutRejectedException instead, which is what was reported.
///
/// These pin the widened budget, and that it is internally consistent: Polly validates the
/// relationship between these three values and throws when the pipeline is first used, which
/// on a device means at the first sync rather than at startup.
/// </remarks>
public class ResilienceBudgetTests
{
    private static HttpStandardResilienceOptions Configured()
    {
        var options = new HttpStandardResilienceOptions();
        ResiliencePolicy.Configure(options);
        return options;
    }

    [Fact]
    public void One_attempt_outlasts_a_serverless_resume()
    {
        // The API holds the request open while EF retries the database connection, so the
        // client's attempt has to outlast the server's whole wake-up, not just a round trip.
        // A resume regularly takes 30 to 60 seconds; the 10-second default never stood a chance.
        Assert.True(Configured().AttemptTimeout.Timeout >= TimeSpan.FromSeconds(45),
            "one attempt must be able to outlast an Azure SQL serverless resume");
    }

    [Fact]
    public void The_total_budget_leaves_room_for_more_than_one_attempt()
    {
        var options = Configured();

        Assert.True(options.TotalRequestTimeout.Timeout >= options.AttemptTimeout.Timeout * 2,
            "a total budget that cannot fit a second attempt makes the retries decorative");
    }

    [Fact]
    public async Task The_options_actually_pass_Pollys_validation()
    {
        // Not a formality. The sampling window must be at least twice the attempt timeout, and
        // widening the attempt without widening the window throws — at first use, on a device,
        // which is the worst possible place to find out.
        var services = new ServiceCollection();
        services.AddHttpClient("budget")
            .ConfigurePrimaryHttpMessageHandler(() => new FlakyHandler(failuresBeforeSuccess: 0))
            .AddStandardResilienceHandler(ResiliencePolicy.Configure);

        var client = services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>().CreateClient("budget");

        var exception = await Record.ExceptionAsync(() => client.GetAsync("https://stub.local/probe"));

        Assert.Null(exception);
    }
}
