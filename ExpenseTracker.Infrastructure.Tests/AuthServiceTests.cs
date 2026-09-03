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
/// IsLoggedInAsync no longer just checks the stored expiry — an expired access token now
/// triggers a silent refresh attempt before answering false. These are what make every
/// existing call site (MainLayout, Settings, SyncService) refresh-aware without any of them
/// changing, since they all already went through this one method.
/// </summary>
public class SilentRefreshTests
{
    private static SyncHarness ExpiredHarness(string? refreshToken = "stub-refresh-token")
    {
        var h = new SyncHarness(signedIn: false);
        h.SignIn(expiry: DateTime.UtcNow.AddHours(-1), refreshToken: refreshToken);
        return h;
    }

    [Fact]
    public async Task An_expired_token_refreshes_silently_when_the_server_accepts_it()
    {
        using var h = ExpiredHarness();

        Assert.True(await h.Auth.IsLoggedInAsync());
        Assert.Equal(1, h.Api.RefreshCallCount);
    }

    [Fact]
    public async Task A_successful_silent_refresh_replaces_the_stored_tokens()
    {
        using var h = ExpiredHarness();

        await h.Auth.IsLoggedInAsync();

        Assert.NotEqual("stub-token", await h.Secrets.GetAsync("jwt_token"));
        Assert.NotEqual("stub-refresh-token", await h.Secrets.GetAsync("jwt_refresh_token"));
    }

    [Fact]
    public async Task A_sync_proceeds_on_an_expired_token_when_refresh_succeeds()
    {
        // The point of the whole feature: sync does not stop working just because a day has
        // passed, as long as the refresh token is still good.
        using var h = ExpiredHarness();

        var result = await h.Sync.SyncAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(1, h.Api.RefreshCallCount);
    }

    [Fact]
    public async Task An_expired_token_with_no_refresh_token_stored_stays_signed_out()
    {
        using var h = ExpiredHarness(refreshToken: null);

        Assert.False(await h.Auth.IsLoggedInAsync());
        Assert.Equal(0, h.Api.RefreshCallCount);
    }

    [Fact]
    public async Task A_rejected_refresh_token_is_cleared_so_it_is_not_retried_forever()
    {
        using var h = ExpiredHarness();
        h.Api.RefreshStatus = HttpStatusCode.Unauthorized;

        Assert.False(await h.Auth.IsLoggedInAsync());
        Assert.Null(await h.Secrets.GetAsync("jwt_refresh_token"));
    }

    [Fact]
    public async Task An_unreachable_server_during_refresh_keeps_the_refresh_token_for_next_time()
    {
        // Not proof the token itself is bad — the same distinction ReasonForStatus draws for
        // login, applied here to whether the stored token survives the attempt.
        using var h = ExpiredHarness();
        h.Api.ThrowOnSend = true;

        Assert.False(await h.Auth.IsLoggedInAsync());
        Assert.Equal("stub-refresh-token", await h.Secrets.GetAsync("jwt_refresh_token"));
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
