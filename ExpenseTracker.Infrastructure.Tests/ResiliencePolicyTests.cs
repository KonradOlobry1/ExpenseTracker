using System.Net;
using Microsoft.Extensions.DependencyInjection;
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
