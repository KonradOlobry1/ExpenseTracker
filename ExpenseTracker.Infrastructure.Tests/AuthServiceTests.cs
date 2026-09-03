using System.Net;
using ExpenseTracker.Application.Interfaces;

namespace ExpenseTracker.Infrastructure.Tests;

/// <summary>
/// AuthResult replaced a bare bool so the UI could tell a wrong password from a locked
/// account from an unreachable server. These pin the mapping from what the API actually
/// returns to the reason the UI branches on — the part a compiler can't check.
/// </summary>
public class LoginFailureMappingTests
{
    [Fact]
    public async Task A_401_maps_to_invalid_credentials()
    {
        using var h = new SyncHarness(signedIn: false);
        h.Api.LoginStatus = HttpStatusCode.Unauthorized;

        var result = await h.Auth.LoginAsync("nobody@test.local", "wrong");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.InvalidCredentials, result.Failure);
    }

    [Fact]
    public async Task A_423_maps_to_account_locked()
    {
        using var h = new SyncHarness(signedIn: false);
        h.Api.LoginStatus = HttpStatusCode.Locked;

        var result = await h.Auth.LoginAsync("someone@test.local", "whatever");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.AccountLocked, result.Failure);
    }

    [Fact]
    public async Task A_500_maps_to_server_error()
    {
        using var h = new SyncHarness(signedIn: false);
        h.Api.LoginStatus = HttpStatusCode.InternalServerError;

        var result = await h.Auth.LoginAsync("someone@test.local", "whatever");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.ServerError, result.Failure);
    }

    [Fact]
    public async Task An_unreachable_server_maps_to_network_error()
    {
        using var h = new SyncHarness(signedIn: false);
        h.Api.ThrowOnSend = true;

        var result = await h.Auth.LoginAsync("someone@test.local", "whatever");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.NetworkError, result.Failure);
    }

    [Fact]
    public async Task A_successful_login_stores_the_token()
    {
        using var h = new SyncHarness(signedIn: false);

        var result = await h.Auth.LoginAsync("someone@test.local", "whatever");

        Assert.True(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.True(await h.Auth.IsLoggedInAsync());
    }

    [Fact]
    public async Task A_400_on_register_maps_to_server_error()
    {
        // Weak password and email-taken both arrive as 400. There is no reason enum granular
        // enough to distinguish them, so both bucket here — see AuthService.ReasonForStatus.
        using var h = new SyncHarness(signedIn: false);
        h.Api.RegisterStatus = HttpStatusCode.BadRequest;

        var result = await h.Auth.RegisterAsync("someone@test.local", "weak");

        Assert.False(result.Succeeded);
        Assert.Equal(AuthFailureReason.ServerError, result.Failure);
    }
}

/// <summary>
/// Sync's own failure mapping, distinct from login's: a sync request can fail after the app
/// already believed it was signed in, which login's mapping never has to account for.
/// </summary>
public class SyncFailureMappingTests
{
    [Fact]
    public async Task A_401_mid_sync_maps_to_session_expired()
    {
        // IsLoggedInAsync said the token was fine a moment ago; the server disagrees now —
        // expiry mid-request, or clock drift between the device and the server.
        using var h = new SyncHarness();
        h.Api.PushStatus = HttpStatusCode.Unauthorized;

        var result = await h.Sync.SyncAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(SyncFailureReason.SessionExpired, result.Failure);
    }

    [Fact]
    public async Task An_unreachable_server_maps_to_network_error()
    {
        using var h = new SyncHarness();
        h.Api.ThrowOnSend = true;

        var result = await h.Sync.SyncAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(SyncFailureReason.NetworkError, result.Failure);
    }
}
