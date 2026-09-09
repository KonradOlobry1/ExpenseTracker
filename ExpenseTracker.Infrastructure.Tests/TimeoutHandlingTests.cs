using ExpenseTracker.Application.Interfaces;
using Polly.Timeout;

namespace ExpenseTracker.Infrastructure.Tests;

/// <summary>
/// What the client does when the resilience pipeline gives up waiting.
/// </summary>
/// <remarks>
/// This was reported from the desktop app as a raw
/// <c>Polly.Timeout.TimeoutRejectedException</c> reaching the user. The cause was not the
/// timeout itself but what the type is: it derives from <c>Polly.ExecutionRejectedException</c>,
/// so it is neither an <see cref="HttpRequestException"/> nor an
/// <see cref="OperationCanceledException"/>, and every catch in AuthService and SyncService
/// filtered on exactly those two. The exception went straight past all of them.
///
/// Which made it the one network failure the app had no answer for. An unreachable server
/// produced a tidy "check your connection"; a *slow* one crashed. These pin the mapping so a
/// timeout is an ordinary failed sync like any other.
/// </remarks>
public class TimeoutHandlingTests
{
    private static TimeoutRejectedException Timeout() =>
        new("The operation didn't complete within the allowed timeout of '00:00:30'.");

    [Fact]
    public async Task A_timed_out_sync_reports_a_network_failure_instead_of_throwing()
    {
        using var h = new SyncHarness();
        h.SignIn();
        h.Api.SendException = Timeout();

        var result = await h.Sync.SyncAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(SyncFailureReason.NetworkError, result.Failure);
    }

    [Fact]
    public async Task A_timed_out_sync_does_not_move_the_last_sync_marker()
    {
        // A sync that never reached the server must not look like one that did, or the next
        // push would send only what changed since a sync that never happened.
        using var h = new SyncHarness();
        h.SignIn();
        h.Api.SendException = Timeout();

        await h.Sync.SyncAsync();

        Assert.Null(h.Sync.LastSyncTime);
    }

    [Fact]
    public async Task A_timed_out_login_reports_a_network_failure_instead_of_throwing()
    {
        using var h = new SyncHarness();
        h.Api.SendException = Timeout();

        var result = await h.Auth.LoginAsync("someone@test.local", "Passw0rd!");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.NetworkError, result.Failure);
    }

    [Fact]
    public async Task A_timed_out_registration_reports_a_network_failure_instead_of_throwing()
    {
        using var h = new SyncHarness();
        h.Api.SendException = Timeout();

        var result = await h.Auth.RegisterAsync("someone@test.local", "Passw0rd!");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.NetworkError, result.Failure);
    }

    [Fact]
    public async Task Signing_out_still_works_when_revoking_times_out()
    {
        // Sign-out must never be blocked by the server being slow. The worst case is a token
        // that lingers server-side until it expires; the user still leaves the app.
        using var h = new SyncHarness();
        h.SignIn();
        h.Api.SendException = Timeout();

        await h.Auth.LogoutAsync();

        Assert.False(await h.Auth.IsLoggedInAsync());
    }
}
