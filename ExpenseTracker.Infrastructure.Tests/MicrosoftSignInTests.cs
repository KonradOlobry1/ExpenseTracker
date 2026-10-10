using System.Net;
using System.Security.Cryptography;
using System.Text;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Contracts;
using ExpenseTracker.Infrastructure.External;

namespace ExpenseTracker.Infrastructure.Tests;

/// <summary>
/// The device half of "Sign in with Microsoft": what it asks the browser to open, what it
/// does with what comes back, and what it refuses to do.
/// </summary>
/// <remarks>
/// The browser is a fake, so the PKCE binding is checked here rather than on a phone: delete
/// these and the app could ship sending the verifier to the browser (making the code
/// redeemable by whoever intercepts the redirect) with every other test still green.
/// </remarks>
public class MicrosoftSignInTests
{
    private sealed class FakeBrowser(IReadOnlyDictionary<string, string>? returns, bool available = true)
        : IExternalAuthBrowser
    {
        public bool IsAvailable => available;
        public Uri? StartedAt { get; private set; }
        public Uri? CallbackExpected { get; private set; }

        public Task<IReadOnlyDictionary<string, string>?> AuthenticateAsync(
            Uri startUrl, Uri callbackUrl, CancellationToken ct = default)
        {
            StartedAt = startUrl;
            CallbackExpected = callbackUrl;
            return Task.FromResult(returns);
        }
    }

    private static AuthService AuthWith(SyncHarness h, IExternalAuthBrowser? browser)
        => new(new HttpClient(h.Api), h.AuthLog, h.DbFactory, h.Prefs, h.Secrets, browser);

    private static Dictionary<string, string> Code(string code = "one-time-code")
        => new() { [ExternalSignIn.CodeParameter] = code };

    [Fact]
    public async Task A_successful_sign_in_stores_the_session_like_a_password_login()
    {
        using var h = new SyncHarness(signedIn: false);
        var auth = AuthWith(h, new FakeBrowser(Code()));

        var result = await auth.SignInWithMicrosoftAsync();

        Assert.True(result.Succeeded);
        Assert.True(await auth.IsLoggedInAsync());
    }

    [Fact]
    public async Task The_browser_gets_only_the_hash_of_the_verifier_and_the_server_gets_the_verifier()
    {
        using var h = new SyncHarness(signedIn: false);
        var browser = new FakeBrowser(Code("abc"));

        await AuthWith(h, browser).SignInWithMicrosoftAsync();

        var exchange = Assert.Single(h.Api.Exchanges);
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(exchange.Verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.Equal("abc", exchange.Code);
        Assert.Contains($"challenge={challenge}", browser.StartedAt!.Query);
        Assert.DoesNotContain(exchange.Verifier, browser.StartedAt.ToString());
        Assert.Equal(ExternalSignIn.CallbackUrl, browser.CallbackExpected!.ToString().TrimEnd('/'));
    }

    [Fact]
    public async Task Backing_out_of_the_browser_is_cancellation_and_signs_nobody_in()
    {
        using var h = new SyncHarness(signedIn: false);
        var auth = AuthWith(h, new FakeBrowser(returns: null));

        var result = await auth.SignInWithMicrosoftAsync();

        Assert.Equal(AuthFailureReason.Cancelled, result.Failure);
        Assert.Empty(h.Api.Exchanges);
        Assert.False(await auth.HasStoredSessionAsync());
    }

    [Fact]
    public async Task An_email_already_held_by_a_password_account_is_reported_as_such()
    {
        using var h = new SyncHarness(signedIn: false);
        var auth = AuthWith(h, new FakeBrowser(
            new Dictionary<string, string> { [ExternalSignIn.ErrorParameter] = ExternalSignIn.EmailInUseError }));

        var result = await auth.SignInWithMicrosoftAsync();

        Assert.Equal(AuthFailureReason.EmailInUse, result.Failure);
        Assert.Empty(h.Api.Exchanges);
    }

    [Fact]
    public async Task A_callback_with_neither_code_nor_error_is_a_failure_not_a_sign_in()
    {
        using var h = new SyncHarness(signedIn: false);
        var auth = AuthWith(h, new FakeBrowser(new Dictionary<string, string>()));

        var result = await auth.SignInWithMicrosoftAsync();

        Assert.Equal(AuthFailureReason.ServerError, result.Failure);
        Assert.False(await auth.HasStoredSessionAsync());
    }

    [Fact]
    public async Task A_rejected_code_does_not_sign_the_device_in()
    {
        using var h = new SyncHarness(signedIn: false);
        h.Api.ExchangeStatus = HttpStatusCode.Unauthorized;
        var auth = AuthWith(h, new FakeBrowser(Code()));

        var result = await auth.SignInWithMicrosoftAsync();

        Assert.Equal(AuthFailureReason.InvalidCredentials, result.Failure);
        Assert.False(await auth.HasStoredSessionAsync());
    }

    [Fact]
    public async Task An_unreachable_server_during_the_exchange_is_a_network_error()
    {
        using var h = new SyncHarness(signedIn: false);
        h.Api.ThrowOnSend = true;

        var result = await AuthWith(h, new FakeBrowser(Code())).SignInWithMicrosoftAsync();

        Assert.Equal(AuthFailureReason.NetworkError, result.Failure);
    }

    [Fact]
    public async Task Without_a_browser_the_capability_is_off_and_sign_in_fails_cleanly()
    {
        using var h = new SyncHarness(signedIn: false);

        foreach (var browser in new IExternalAuthBrowser?[] { null, new FakeBrowser(Code(), available: false) })
        {
            var auth = AuthWith(h, browser);

            Assert.False(auth.SupportsMicrosoftSignIn);
            Assert.False((await auth.SignInWithMicrosoftAsync()).Succeeded);
        }
    }

    [Fact]
    public void A_usable_browser_turns_the_capability_on()
    {
        using var h = new SyncHarness(signedIn: false);

        Assert.True(AuthWith(h, new FakeBrowser(Code())).SupportsMicrosoftSignIn);
    }
}
