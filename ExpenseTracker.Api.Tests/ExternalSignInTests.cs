using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ExpenseTracker.Api.Auth;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExpenseTracker.Api.Tests.TestClient;

namespace ExpenseTracker.Api.Tests;

/// <summary>The API with an Entra app registration configured (placeholder values, never contacted).</summary>
public class EntraFactory : ApiFactory
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Entra:ClientId", "00000000-0000-0000-0000-000000000000");
        builder.UseSetting("Entra:ClientSecret", "not-a-real-secret");
    }
}

/// <summary>
/// "Sign in with Microsoft": who ends up with which account, and what the app's one-time code
/// will and will not buy.
/// </summary>
/// <remarks>
/// The redirect to Entra itself is not exercised - it needs the provider's discovery document
/// over the network, and a test that depends on login.microsoftonline.com being reachable fails
/// for reasons that have nothing to do with this code. What is covered is everything after the
/// identity is verified, which is where the account-takeover mistakes live: linking by email,
/// duplicate accounts, and a code that works without its verifier. Delete these and a change
/// that merges accounts by email, or accepts a bare code, passes every other suite.
/// </remarks>
public class ExternalSignInTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static (string Verifier, string Challenge) NewPkce()
    {
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static ExternalLoginInfo MicrosoftIdentity(string subject, string? email)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, subject) };
        if (email is not null) claims.Add(new Claim(ClaimTypes.Email, email));

        return new ExternalLoginInfo(
            new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
            ExternalAuthEndpoints.Scheme, subject, "Microsoft");
    }

    private async Task<ExternalLoginResult> ResolveAsync(ExternalLoginInfo info)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ExternalLogins>().ResolveAsync(info, default);
    }

    private async Task<string> UserIdOfAsync(string email)
    {
        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByEmailAsync(email);
        return user!.Id;
    }

    [Fact]
    public async Task A_new_microsoft_identity_gets_its_own_account_with_the_default_categories()
    {
        var email = $"ms-{Guid.NewGuid():N}@test.local";

        var result = await ResolveAsync(MicrosoftIdentity(Guid.NewGuid().ToString(), email));

        Assert.True(result.Succeeded);
        Assert.Equal(email, result.User!.Email);

        // Without categories no expense could ever be created, as with every other sign-up path.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApiDbContext>();
        Assert.True(await db.Categories.AnyAsync(c => c.UserId == result.User.Id));
    }

    [Fact]
    public async Task The_same_microsoft_identity_returns_to_the_same_account()
    {
        var subject = Guid.NewGuid().ToString();
        var email = $"ms-{Guid.NewGuid():N}@test.local";

        var first = await ResolveAsync(MicrosoftIdentity(subject, email));
        var second = await ResolveAsync(MicrosoftIdentity(subject, email));

        Assert.Equal(first.User!.Id, second.User!.Id);
    }

    [Fact]
    public async Task A_returning_identity_is_found_by_subject_even_if_its_email_changed()
    {
        // Matching on email would turn a renamed mailbox into a brand-new empty account.
        var subject = Guid.NewGuid().ToString();
        var first = await ResolveAsync(MicrosoftIdentity(subject, $"old-{Guid.NewGuid():N}@test.local"));

        var second = await ResolveAsync(MicrosoftIdentity(subject, $"new-{Guid.NewGuid():N}@test.local"));

        Assert.Equal(first.User!.Id, second.User!.Id);
    }

    [Fact]
    public async Task A_microsoft_identity_is_not_merged_into_a_password_account_with_the_same_email()
    {
        // The Entra email claim is not proof of ownership, so a match must never hand over the
        // password account's data.
        var email = $"taken-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAsync(email);

        var result = await ResolveAsync(MicrosoftIdentity(Guid.NewGuid().ToString(), email));

        Assert.False(result.Succeeded);
        Assert.Equal(ExternalLoginFailure.EmailInUse, result.Failure);

        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var owner = await users.FindByEmailAsync(email);
        Assert.Empty(await users.GetLoginsAsync(owner!));
    }

    [Fact]
    public async Task An_identity_with_no_usable_email_is_refused_rather_than_given_a_blank_account()
    {
        var result = await ResolveAsync(MicrosoftIdentity(Guid.NewGuid().ToString(), email: null));

        Assert.False(result.Succeeded);
        Assert.Equal(ExternalLoginFailure.NoEmail, result.Failure);
    }

    [Fact]
    public async Task A_locked_out_account_cannot_sign_in_through_microsoft()
    {
        var subject = Guid.NewGuid().ToString();
        var email = $"ms-{Guid.NewGuid():N}@test.local";
        var created = await ResolveAsync(MicrosoftIdentity(subject, email));

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = await users.FindByIdAsync(created.User!.Id);
            await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddMinutes(10));
        }

        var again = await ResolveAsync(MicrosoftIdentity(subject, email));

        Assert.Equal(ExternalLoginFailure.Locked, again.Failure);
    }

    [Fact]
    public async Task The_device_code_and_its_verifier_exchange_for_a_working_session()
    {
        var email = $"dev-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAsync(email);
        var (verifier, challenge) = NewPkce();
        var code = factory.Services.GetRequiredService<DeviceSignInCodes>()
            .Issue(await UserIdOfAsync(email), challenge);

        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(ExternalSignIn.ExchangePath,
            new ExternalExchangeRequest(code, verifier));
        var auth = await response.ReadAsync<AuthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/sync/pull")).StatusCode);
        Assert.False(string.IsNullOrEmpty(auth.RefreshToken));
    }

    [Fact]
    public async Task An_intercepted_code_is_useless_without_the_verifier()
    {
        // The custom URL scheme can be claimed by any installed app, so the code alone leaks.
        var email = $"dev-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAsync(email);
        var (_, challenge) = NewPkce();
        var (otherVerifier, _) = NewPkce();
        var code = factory.Services.GetRequiredService<DeviceSignInCodes>()
            .Issue(await UserIdOfAsync(email), challenge);

        var response = await factory.CreateClient().PostAsJsonAsync(ExternalSignIn.ExchangePath,
            new ExternalExchangeRequest(code, otherVerifier));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_forged_code_is_refused()
    {
        var (verifier, _) = NewPkce();

        var response = await factory.CreateClient().PostAsJsonAsync(ExternalSignIn.ExchangePath,
            new ExternalExchangeRequest("not-a-real-code", verifier));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_code_for_a_locked_out_account_is_refused()
    {
        // The callback checks lockout, but the code outlives that check by its lifetime.
        var email = $"dev-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAsync(email);
        var (verifier, challenge) = NewPkce();
        var userId = await UserIdOfAsync(email);
        var code = factory.Services.GetRequiredService<DeviceSignInCodes>().Issue(userId, challenge);

        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            await users.SetLockoutEndDateAsync((await users.FindByIdAsync(userId))!, DateTimeOffset.UtcNow.AddMinutes(10));
        }

        var response = await factory.CreateClient().PostAsJsonAsync(ExternalSignIn.ExchangePath,
            new ExternalExchangeRequest(code, verifier));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task The_entry_points_do_not_exist_when_entra_is_not_configured()
    {
        // Redirecting to a provider that was never registered would strand the user on a
        // Microsoft error page.
        var (_, challenge) = NewPkce();
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/account/external/microsoft")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"{ExternalSignIn.StartPath}?challenge={challenge}")).StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("this-is-43-characters-but-has-a-bad-char-in-it!!")]
    public void Only_a_well_formed_pkce_challenge_is_accepted(string challenge)
        => Assert.False(DeviceSignInCodes.IsValidChallenge(challenge));

    [Fact]
    public void A_real_pkce_challenge_is_accepted()
        => Assert.True(DeviceSignInCodes.IsValidChallenge(NewPkce().Challenge));
}

/// <summary>
/// The app starts and the login page offers the button only when Entra is configured.
/// </summary>
/// <remarks>
/// Booting with the OpenID Connect handler registered is itself the check that the scheme wires
/// into Identity's external cookie; a mistake there fails every request, not just this one.
/// </remarks>
public class EntraConfiguredTests(EntraFactory factory) : IClassFixture<EntraFactory>
{
    [Fact]
    public async Task The_login_page_offers_microsoft_when_it_is_configured()
    {
        var html = await factory.CreateClient().GetStringAsync("/account/login");

        Assert.Contains("/account/external/microsoft", html);
    }

    [Fact]
    public async Task The_login_page_does_not_offer_microsoft_when_it_is_not_configured()
    {
        using var plain = new ApiFactory();

        var html = await plain.CreateClient().GetStringAsync("/account/login");

        Assert.DoesNotContain("/account/external/microsoft", html);
    }
}
