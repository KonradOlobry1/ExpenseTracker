using System.Security.Claims;
using ExpenseTracker.Application.Interfaces;
using Microsoft.JSInterop;

namespace ExpenseTracker.Api.Web;

/// <summary>
/// Reports the signed-in account to the shared components, based on the auth cookie.
/// </summary>
/// <remarks>
/// Sign-in and sign-out are deliberately not implemented here. Setting or clearing an auth
/// cookie writes a response header, which is impossible from inside an interactive Blazor
/// circuit — the response has long since been sent. The web app therefore signs in through
/// the statically rendered page at /account/login, which is a real HTTP request.
///
/// The MAUI implementation of this interface does the equivalent over HTTP against the sync
/// API and stores a JWT in SecureStorage.
/// </remarks>
public class WebAuthService(IHttpContextAccessor accessor, IJSRuntime js) : IAuthService
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    /// <summary>Not meaningful on the web — the UI and the database are the same deployment.</summary>
    public string ApiBaseUrl => string.Empty;

    public Task<bool> IsLoggedInAsync()
        => Task.FromResult(User?.Identity?.IsAuthenticated ?? false);

    /// <summary>
    /// The same thing on the web. The distinction the interface draws exists for the device,
    /// where a stored token can outlive its expiry; a cookie is either presented or it is not,
    /// and the Blazor endpoints already require authorization before a circuit starts.
    /// </summary>
    public Task<bool> HasStoredSessionAsync() => IsLoggedInAsync();

    public Task<UserInfo?> GetCurrentUserAsync()
    {
        if (User?.Identity?.IsAuthenticated != true) return Task.FromResult<UserInfo?>(null);

        return Task.FromResult<UserInfo?>(new UserInfo(
            User.FindFirstValue(ClaimTypes.Email) ?? User.Identity.Name ?? string.Empty,
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty));
    }

    /// <summary>Cookie auth carries no bearer token.</summary>
    public Task<string?> GetTokenAsync() => Task.FromResult<string?>(null);

    public Task<AuthResult> LoginAsync(string email, string password, CancellationToken ct = default)
        => throw new NotSupportedException("Sign in via /account/login — a cookie cannot be set from a Blazor circuit.");

    public Task<AuthResult> RegisterAsync(string email, string password, CancellationToken ct = default)
        => throw new NotSupportedException("Register via /account/login — a cookie cannot be set from a Blazor circuit.");

    /// <summary>
    /// Posts to /account/logout through JS, rather than clearing the cookie inline.
    /// </summary>
    /// <remarks>
    /// This used to throw NotSupportedException, on the reasoning that a circuit cannot clear
    /// an auth cookie so the caller should go to the endpoint itself. That reasoning was right
    /// and the throw was still wrong: the shared Settings page calls this from a live circuit,
    /// and an unhandled exception there does not just fail the click — it terminates the whole
    /// circuit. Every button on every page then went dead until a manual reload, which is what
    /// "no reaction on any button" turned out to be.
    ///
    /// Signing in has the same constraint and solves it with a statically rendered page. Sign
    /// out has no page of its own, so it submits a form instead — a real request, a real
    /// response, and the endpoint's redirect lands on the sign-in page.
    /// </remarks>
    public async Task LogoutAsync() => await js.InvokeVoidAsync("appAuth.signOut");
}
