using System.Net;
using System.Net.Http.Json;

namespace ExpenseTracker.Api.Tests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Register_issues_a_token()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = "register@test.local", Password = "Passw0rd!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.ReadAsync<TestClient.AuthResponse>();
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));
        Assert.True(auth.Expiry > DateTime.UtcNow);
    }

    [Fact]
    public async Task Registering_the_same_email_twice_is_rejected()
    {
        var client = factory.CreateClient();
        var body = new { Email = "duplicate@test.local", Password = "Passw0rd!" };

        await client.PostAsJsonAsync("/api/auth/register", body);
        var second = await client.PostAsJsonAsync("/api/auth/register", body);

        Assert.NotEqual(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Login_succeeds_with_the_right_password()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new { Email = "login@test.local", Password = "Passw0rd!" });

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "login@test.local", Password = "Passw0rd!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_fails_with_the_wrong_password()
    {
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/register",
            new { Email = "wrongpass@test.local", Password = "Passw0rd!" });

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "wrongpass@test.local", Password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_fails_for_an_unknown_account()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "nobody@test.local", Password = "Passw0rd!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/sync/pull")]
    public async Task Sync_endpoints_reject_anonymous_callers(string url)
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_garbage_bearer_token_is_rejected()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer not-a-real-jwt");

        var response = await client.GetAsync("/api/sync/pull");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
