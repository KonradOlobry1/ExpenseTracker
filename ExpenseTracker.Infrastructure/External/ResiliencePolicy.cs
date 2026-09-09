using Microsoft.Extensions.Http.Resilience;

namespace ExpenseTracker.Infrastructure.External;

/// <summary>
/// The retry and timeout budget every device-to-API call runs under.
/// </summary>
/// <remarks>
/// <para>
/// The defaults of <c>AddStandardResilienceHandler()</c> cannot survive the one thing this
/// policy exists for. They allow 10 seconds per attempt and 30 seconds in total, while the
/// database this app talks to is on Azure SQL's serverless tier: it auto-pauses when idle, and
/// resuming regularly takes 30 to 60 seconds. The API holds the request open while EF retries
/// the connection, so the client's attempt has to outlast the *server's* whole wake-up, not
/// just its own network round trip.
/// </para>
/// <para>
/// The result was a request that could not succeed. Each attempt was killed at 10 seconds,
/// well before the database finished resuming; three of those exhausted the 30-second total
/// budget, and the caller got a TimeoutRejectedException — the first sync after an idle period
/// failing every time, which is exactly the case the policy was added to cover.
/// </para>
/// <para>
/// So the lever is a longer attempt, not more of them. Retrying faster does not help when every
/// attempt is cut short before the thing it is waiting for can finish.
/// </para>
/// </remarks>
public static class ResiliencePolicy
{
    /// <summary>Long enough to outlast a serverless resume plus an App Service cold start.</summary>
    public static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Room for two full attempts and the backoff between them.</summary>
    public static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(100);

    public static void Configure(HttpStandardResilienceOptions options)
    {
        options.AttemptTimeout.Timeout = AttemptTimeout;
        options.TotalRequestTimeout.Timeout = TotalTimeout;

        // Polly validates this: the sampling window must be at least twice the attempt timeout,
        // or the pipeline throws when it is first used — which on a device would be at the
        // first sync, not at startup.
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(90);

        // Two retries rather than three. With 45-second attempts a third could not fit inside
        // the total budget anyway, so it would only ever be cut off mid-flight.
        options.Retry.MaxRetryAttempts = 2;
    }
}
