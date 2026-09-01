using System.Net;
using System.Net.Http.Json;
using static ExpenseTracker.Api.Tests.TestClient;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// The Identity password policy was relaxed during development. These pin it back.
/// </summary>
public class PasswordPolicyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Theory]
    [InlineData("short1A", "under eight characters")]
    [InlineData("alllowercase1", "no uppercase")]
    [InlineData("ALLUPPERCASE1", "no lowercase")]
    [InlineData("NoDigitsHere", "no digit")]
    public async Task Weak_passwords_are_rejected(string password, string _)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = $"weak-{Guid.NewGuid():N}@test.local", Password = password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_password_meeting_the_policy_is_accepted()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = $"strong-{Guid.NewGuid():N}@test.local", Password = "Passw0rd!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Registering_through_the_api_seeds_the_built_in_categories()
    {
        // The web registration path already did this; the API path did not, so an account
        // created from the phone had no categories server-side.
        var client = await factory.RegisterAsync();

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        Assert.Equal(7, pulled!.Categories!.Count);
        Assert.Contains(pulled.Categories, c => c.Name == "Food");
    }
}

/// <summary>
/// Its own factory: lockout counters live in the database this instance owns, so sharing one
/// with other tests would make the attempt counts non-deterministic.
/// </summary>
public class LockoutTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public LockoutTests(ApiFactory factory) => _factory = factory;

    private const string Password = "Passw0rd!";

    private async Task<string> RegisterAsync(HttpClient client)
    {
        var email = $"lock-{Guid.NewGuid():N}@test.local";
        var created = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password });
        created.EnsureSuccessStatusCode();
        return email;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
        => client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });

    [Fact]
    public async Task Repeated_failures_lock_the_account()
    {
        var client = _factory.CreateClient();
        var email = await RegisterAsync(client);

        // MaxFailedAccessAttempts is 5, so the fifth failure trips the lockout.
        HttpResponseMessage? last = null;
        for (var i = 0; i < 5; i++)
            last = await LoginAsync(client, email, "WrongPassword1");

        Assert.Equal(HttpStatusCode.Locked, last!.StatusCode);
    }

    [Fact]
    public async Task A_locked_account_rejects_even_the_correct_password()
    {
        var client = _factory.CreateClient();
        var email = await RegisterAsync(client);

        for (var i = 0; i < 5; i++)
            await LoginAsync(client, email, "WrongPassword1");

        var response = await LoginAsync(client, email, Password);

        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_email_and_a_wrong_password_are_indistinguishable()
    {
        // Otherwise the endpoint confirms which addresses have accounts.
        var client = _factory.CreateClient();
        var email = await RegisterAsync(client);

        var wrongPassword = await LoginAsync(client, email, "WrongPassword1");
        var unknownEmail = await LoginAsync(client, $"nobody-{Guid.NewGuid():N}@test.local", "WrongPassword1");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(
            await wrongPassword.Content.ReadAsStringAsync(),
            await unknownEmail.Content.ReadAsStringAsync());
    }
}

/// <summary>
/// Lowers the limit to something a test can reach. All requests share one partition here,
/// since they all originate from the same loopback address.
/// </summary>
public class RateLimitFactory : ApiFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("RateLimit:AuthPermitPerMinute", "5");
    }
}

public class RateLimitTests(RateLimitFactory factory) : IClassFixture<RateLimitFactory>
{
    [Fact]
    public async Task Auth_endpoints_reject_a_burst()
    {
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { Email = "burst@test.local", Password = "WrongPassword1" });
            statuses.Add(response.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task Requests_within_the_limit_are_not_throttled()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = "within@test.local", Password = "WrongPassword1" });

        Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
    }
}
