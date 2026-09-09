using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExpenseTracker.Api.Tests.TestClient;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// Captures reset tokens instead of mailing them, so a test can act on the link a user would
/// have received.
/// </summary>
/// <remarks>
/// Going through the real sender seam rather than reading the database is the point: tokens are
/// stored hashed, so the raw value genuinely cannot be recovered from storage. If the controller
/// ever stopped calling the sender, these tests would fail rather than quietly exercising a
/// token no real user could ever have obtained.
/// </remarks>
public class CapturingPasswordResetSender : IPasswordResetSender
{
    private readonly ConcurrentDictionary<string, string> _sent = new();

    public Task SendAsync(string email, string token, CancellationToken ct = default)
    {
        _sent[email] = token;
        return Task.CompletedTask;
    }

    public string? TokenFor(string email) => _sent.GetValueOrDefault(email);

    public bool SentAnythingTo(string email) => _sent.ContainsKey(email);
}

public class PasswordResetFactory : ApiFactory
{
    public CapturingPasswordResetSender Sender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            services.AddScoped<IPasswordResetSender>(_ => Sender));
    }
}

/// <summary>
/// Recovering an account whose password is lost, without that recovery becoming a second way in.
/// </summary>
/// <remarks>
/// The flow is only as good as its refusals, so most of these cover what it declines to do:
/// reveal which addresses are registered, accept a token twice, honour an expired one, skip the
/// password policy, or leave a thief holding a live session after the owner resets. Delete them
/// and the endpoint still passes its happy path.
/// </remarks>
public class PasswordResetTests(PasswordResetFactory factory) : IClassFixture<PasswordResetFactory>
{
    private const string OriginalPassword = "Passw0rd!";
    private const string NewPassword = "N3wPassw0rd!";

    private static Task<HttpResponseMessage> ForgotAsync(HttpClient client, string email)
        => client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = email });

    private static Task<HttpResponseMessage> ResetAsync(
        HttpClient client, string token, string password)
        => client.PostAsJsonAsync("/api/auth/reset-password",
            new { Token = token, NewPassword = password });

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client, string email, string password)
        => client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });

    private async Task<(HttpClient Client, string Email)> RegisterAsync()
    {
        var email = $"reset-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = OriginalPassword });
        response.EnsureSuccessStatusCode();

        return (client, email);
    }

    /// <summary>Registers an account and returns the token a reset would have mailed to it.</summary>
    private async Task<(HttpClient Client, string Email, string Token)> RequestResetAsync()
    {
        var (client, email) = await RegisterAsync();
        await ForgotAsync(client, email);

        return (client, email, factory.Sender.TokenFor(email)!);
    }

    [Fact]
    public async Task A_reset_request_for_a_real_account_sends_a_token()
    {
        var (_, email, token) = await RequestResetAsync();

        Assert.False(string.IsNullOrWhiteSpace(token));
        Assert.True(factory.Sender.SentAnythingTo(email));
    }

    [Fact]
    public async Task An_unknown_address_gets_the_same_answer_and_no_mail()
    {
        // This endpoint takes no credentials and anyone can call it, so a 404 for unknown
        // addresses would enumerate the entire user table for free.
        var client = factory.CreateClient();
        var unknown = $"nobody-{Guid.NewGuid():N}@test.local";

        var response = await ForgotAsync(client, unknown);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(factory.Sender.SentAnythingTo(unknown));
    }

    [Fact]
    public async Task The_token_sets_a_new_password_that_works()
    {
        var (client, email, token) = await RequestResetAsync();

        var reset = await ResetAsync(client, token, NewPassword);
        var login = await LoginAsync(client, email, NewPassword);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task The_old_password_stops_working()
    {
        var (client, email, token) = await RequestResetAsync();

        await ResetAsync(client, token, NewPassword);
        var login = await LoginAsync(client, email, OriginalPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task A_token_cannot_be_used_twice()
    {
        // The link lands in a mailbox and stays there. Single use means a copy read later, by
        // whoever else can reach that mailbox, is worth nothing.
        var (client, _, token) = await RequestResetAsync();

        await ResetAsync(client, token, NewPassword);
        var second = await ResetAsync(client, token, "An0therPass!");

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [Fact]
    public async Task Requesting_a_second_link_retires_the_first()
    {
        var (client, email, first) = await RequestResetAsync();

        await ForgotAsync(client, email);
        var second = factory.Sender.TokenFor(email);

        var withFirst = await ResetAsync(client, first, NewPassword);

        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.BadRequest, withFirst.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var (client, _, token) = await RequestResetAsync();
        await BackdateExpiryAsync(token);

        var response = await ResetAsync(client, token, NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_token_is_rejected()
    {
        var response = await ResetAsync(factory.CreateClient(), "not-a-real-token", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_weak_new_password_is_rejected()
    {
        // Reset must not be a side door around the policy registration enforces.
        var (client, _, token) = await RequestResetAsync();

        var response = await ResetAsync(client, token, "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_rejected_password_does_not_spend_the_token()
    {
        // One typo must not cost the user their only link and send them round the flow again.
        var (client, _, token) = await RequestResetAsync();

        await ResetAsync(client, token, "short");
        var retry = await ResetAsync(client, token, NewPassword);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Resetting_kills_the_sessions_that_existed_before_it()
    {
        // The reason to reset a password is usually that someone else knows it. If a refresh
        // token issued earlier keeps working, the reset achieves nothing for up to thirty days.
        var email = $"reset-{Guid.NewGuid():N}@test.local";
        var client = factory.CreateClient();

        var registration = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = OriginalPassword });
        var existingSession = (await registration.ReadAsync<AuthResponse>())!;

        await ForgotAsync(client, email);
        await ResetAsync(client, factory.Sender.TokenFor(email)!, NewPassword);

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new { RefreshToken = existingSession.RefreshToken });

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Resetting_clears_a_lockout()
    {
        // A locked-out user is precisely the user who reaches for "forgot password". If the
        // lockout survived the reset, the new password would look broken for fifteen minutes.
        var (client, email) = await RegisterAsync();

        for (var i = 0; i < 6; i++)
            await LoginAsync(client, email, "WrongPassw0rd!");

        var lockedOut = await LoginAsync(client, email, OriginalPassword);

        await ForgotAsync(client, email);
        await ResetAsync(client, factory.Sender.TokenFor(email)!, NewPassword);
        var afterReset = await LoginAsync(client, email, NewPassword);

        Assert.Equal(HttpStatusCode.Locked, lockedOut.StatusCode);
        Assert.Equal(HttpStatusCode.OK, afterReset.StatusCode);
    }

    [Fact]
    public async Task The_token_is_stored_hashed_and_never_in_the_clear()
    {
        // Same property the refresh tokens hold: a database read — a backup, a leaked
        // connection string — must not hand out a working credential.
        var (_, _, token) = await RequestResetAsync();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        var hashes = await db.PasswordResetTokens.Select(t => t.TokenHash).ToListAsync();

        Assert.DoesNotContain(token, hashes);
        Assert.Contains(Hash(token), hashes);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>
    /// Reaches into the database, the same way the refresh-token tests do — there is no API
    /// surface for backdating a token and there should not be one just to make a test possible.
    /// </summary>
    private async Task BackdateExpiryAsync(string token)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();

        var hash = Hash(token);
        var stored = await db.PasswordResetTokens.SingleAsync(t => t.TokenHash == hash);
        stored.ExpiresAt = DateTime.UtcNow.AddHours(-2);
        await db.SaveChangesAsync();
    }
}
