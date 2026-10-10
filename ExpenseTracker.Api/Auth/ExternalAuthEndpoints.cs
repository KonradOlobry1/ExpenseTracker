using ExpenseTracker.Api.Models;
using ExpenseTracker.Api.Web;
using ExpenseTracker.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Api.Auth;

/// <summary>
/// The browser-facing half of "Sign in with Microsoft": where the challenge starts and where
/// the user lands afterwards. Used by both the web UI and the device flow.
/// </summary>
/// <remarks>
/// Two entry points, one callback. They differ only in what happens once the identity is
/// verified: the web signs the cookie in and returns to the site, the device mints a one-time
/// code and returns to the app. The callback tells them apart by a PKCE challenge stashed in
/// the authentication properties, which round-trips through Entra inside the protected state
/// and so cannot be set by anyone but the server.
///
/// Neither entry point exists when Entra is not configured - they answer 404 rather than
/// redirecting a user to a provider that is not registered.
/// </remarks>
public static class ExternalAuthEndpoints
{
    public const string Scheme = "Microsoft";
    public const string CallbackPath = "/account/external/callback";
    public const string LinkPath = "/account/external/link";
    public const string LinkCallbackPath = "/account/external/link/callback";

    private const string DeviceChallengeItem = "device_challenge";

    public static void MapExternalAuth(this WebApplication app)
    {
        app.MapGet("/account/external/microsoft", (
                string? returnUrl, EntraOptions entra, SignInManager<AppUser> signIn) =>
            {
                if (!entra.Enabled) return Results.NotFound();

                var properties = signIn.ConfigureExternalAuthenticationProperties(
                    Scheme, $"{CallbackPath}?returnUrl={Uri.EscapeDataString(LocalOrRoot(returnUrl))}");
                return Results.Challenge(properties, [Scheme]);
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        app.MapGet(ExternalSignIn.StartPath, (
                string? challenge, EntraOptions entra, SignInManager<AppUser> signIn) =>
            {
                if (!entra.Enabled) return Results.NotFound();
                if (!DeviceSignInCodes.IsValidChallenge(challenge)) return Results.BadRequest();

                var properties = signIn.ConfigureExternalAuthenticationProperties(Scheme, CallbackPath);
                properties.Items[DeviceChallengeItem] = challenge;
                return Results.Challenge(properties, [Scheme]);
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        app.MapGet(CallbackPath, async (
                HttpContext http, string? returnUrl, EntraOptions entra,
                SignInManager<AppUser> signIn, ExternalLogins logins, DeviceSignInCodes codes,
                CancellationToken ct) =>
            {
                if (!entra.Enabled) return Results.NotFound();

                var info = await signIn.GetExternalLoginInfoAsync();
                if (info is null)
                    return Results.Redirect("/account/login?external=failed");

                // The external cookie is only a courier for this one round trip.
                await http.SignOutAsync(IdentityConstants.ExternalScheme);

                string? deviceChallenge = null;
                info.AuthenticationProperties?.Items.TryGetValue(DeviceChallengeItem, out deviceChallenge);
                var result = await logins.ResolveAsync(info, ct);

                if (deviceChallenge is not null)
                {
                    return Results.Redirect(result.User is { } deviceUser
                        ? $"{ExternalSignIn.CallbackUrl}?{ExternalSignIn.CodeParameter}="
                          + Uri.EscapeDataString(codes.Issue(deviceUser.Id, deviceChallenge))
                        : $"{ExternalSignIn.CallbackUrl}?{ExternalSignIn.ErrorParameter}="
                          + ErrorCode(result.Failure));
                }

                if (result.User is not { } user)
                    return Results.Redirect($"/account/login?external={ErrorCode(result.Failure)}");

                await signIn.SignInAsync(user, isPersistent: true);
                SettingsCookies.Write(http.Response, user);
                return Results.Redirect(LocalOrRoot(returnUrl));
            })
            .AllowAnonymous()
            .RequireRateLimiting("auth");

        MapLink(app);
    }

    /// <summary>
    /// Settings -> "Link Microsoft account". Unlike sign-in these require an existing session:
    /// the point is to attach an identity to the account the user is already in.
    /// </summary>
    private static void MapLink(WebApplication app)
    {
        app.MapGet(LinkPath, async (
                HttpContext http, EntraOptions entra,
                SignInManager<AppUser> signIn, UserManager<AppUser> users) =>
            {
                if (!entra.Enabled) return Results.NotFound();
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();

                // The user id rides along as the anti-forgery value. The callback only accepts
                // an identity if it comes back carrying the id of whoever is signed in then, so
                // a flow somebody else started cannot be completed inside this session.
                var properties = signIn.ConfigureExternalAuthenticationProperties(
                    Scheme, LinkCallbackPath, user.Id);
                return Results.Challenge(properties, [Scheme]);
            })
            .RequireAuthorization()
            .RequireRateLimiting("auth");

        app.MapGet(LinkCallbackPath, async (
                HttpContext http, EntraOptions entra, SignInManager<AppUser> signIn,
                UserManager<AppUser> users, ExternalLogins logins) =>
            {
                if (!entra.Enabled) return Results.NotFound();
                if (await users.GetUserAsync(http.User) is not { } user) return Results.Unauthorized();

                var info = await signIn.GetExternalLoginInfoAsync(user.Id);
                await http.SignOutAsync(IdentityConstants.ExternalScheme);

                if (info is null)
                    return Results.Redirect("/settings?link=failed");

                return Results.Redirect(await logins.LinkAsync(user, info) switch
                {
                    ExternalLinkResult.Linked or ExternalLinkResult.AlreadyLinked => "/settings?link=linked",
                    ExternalLinkResult.TakenByOther => "/settings?link=taken",
                    _ => "/settings?link=failed",
                });
            })
            .RequireAuthorization()
            .RequireRateLimiting("auth");
    }

    /// <summary>
    /// The value carried back to the login page (and to the app) to say what went wrong.
    /// Deliberately coarse: it is read by a script on the other end of a URL anyone can edit.
    /// </summary>
    public static string ErrorCode(ExternalLoginFailure? failure) => failure switch
    {
        ExternalLoginFailure.EmailInUse => ExternalSignIn.EmailInUseError,
        ExternalLoginFailure.Locked => "locked",
        ExternalLoginFailure.NoEmail => "no_email",
        _ => "failed",
    };

    // Only ever redirect to a path on this site - never to a caller-supplied absolute URL.
    // "//host" and "/\host" are protocol-relative to a browser, so a leading slash is not enough.
    private static string LocalOrRoot(string? returnUrl) =>
        !string.IsNullOrWhiteSpace(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
            ? returnUrl
            : "/";
}
