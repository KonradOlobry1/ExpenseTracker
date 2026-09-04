using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ExpenseTracker.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExpenseTracker.Api.Tests.TestClient;

namespace ExpenseTracker.Api.Tests;

public class RefreshTokenTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken)
        => client.PostAsJsonAsync("/api/auth/refresh", new { RefreshToken = refreshToken });

    private static Task<HttpResponseMessage> RevokeAsync(HttpClient client, string refreshToken)
        => client.PostAsJsonAsync("/api/auth/revoke", new { RefreshToken = refreshToken });

    [Fact]
    public async Task Refreshing_a_valid_token_issues_a_new_pair()
    {
        var (client, auth) = await factory.RegisterWithTokensAsync();

        var response = await RefreshAsync(client, auth.RefreshToken);
        var newAuth = await response.ReadAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(auth.Token, newAuth!.Token);
        Assert.NotEqual(auth.RefreshToken, newAuth.RefreshToken);
    }

    [Fact]
    public async Task The_new_access_token_actually_authorizes_requests()
    {
        var (client, auth) = await factory.RegisterWithTokensAsync();
        var newAuth = await (await RefreshAsync(client, auth.RefreshToken)).ReadAsync<AuthResponse>();

        var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAuth!.Token);

        var response = await anonymous.GetAsync("/api/sync/pull");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_used_refresh_token_does_not_work_a_second_time()
    {
        // Rotation: the old token is spent the moment it refreshes, whether or not the
        // caller reuses it. A reused copy fails exactly like a stolen one would.
        var (client, auth) = await factory.RegisterWithTokensAsync();

        await RefreshAsync(client, auth.RefreshToken);
        var secondAttempt = await RefreshAsync(client, auth.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task An_unknown_token_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await RefreshAsync(client, "not-a-real-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var (client, auth) = await factory.RegisterWithTokensAsync();
        await BackdateExpiryAsync(auth.RefreshToken);

        var response = await RefreshAsync(client, auth.RefreshToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Revoking_a_token_then_trying_to_refresh_with_it_fails()
    {
        var (client, auth) = await factory.RegisterWithTokensAsync();

        var revoke = await RevokeAsync(client, auth.RefreshToken);
        var refresh = await RefreshAsync(client, auth.RefreshToken);

        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Revoking_an_unknown_token_still_returns_success()
    {
        // Anonymous caller, nothing useful to report either way — see AuthController.Revoke.
        var client = factory.CreateClient();

        var response = await RevokeAsync(client, "not-a-real-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>Reaches directly into the database — there is no API surface for backdating
    /// a token, and there shouldn't be one just to make this test possible.</summary>
    private async Task BackdateExpiryAsync(string refreshToken)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
        var stored = await db.RefreshTokens.SingleAsync(r => r.TokenHash == hash);
        stored.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
    }
}
