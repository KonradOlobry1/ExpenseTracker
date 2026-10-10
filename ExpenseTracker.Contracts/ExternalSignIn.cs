namespace ExpenseTracker.Contracts;

/// <summary>
/// The pieces of the device's "Sign in with Microsoft" flow that the server and the app have
/// to agree on.
/// </summary>
/// <remarks>
/// The device never talks to Entra. It opens <see cref="StartPath"/> in the system browser, the
/// server runs the OpenID Connect exchange, and the browser is sent back to
/// <see cref="CallbackUrl"/> carrying a short-lived one-time code, which the app trades for the
/// same access/refresh token pair a password sign-in returns. Keeping Entra on the server means
/// the client id and secret never ship inside an app package.
///
/// The callback is a fixed constant on both sides, not a parameter: a server that redirected to
/// whatever address the caller named would hand a signed-in code to anyone.
/// </remarks>
public static class ExternalSignIn
{
    public const string CallbackScheme = "expensetracker";
    public const string CallbackUrl = "expensetracker://auth";

    public const string StartPath = "/api/auth/external/microsoft/start";
    public const string ExchangePath = "/api/auth/external/exchange";

    /// <summary>Query parameter on the callback that carries the one-time code.</summary>
    public const string CodeParameter = "code";

    /// <summary>Query parameter on the callback that carries a failure reason instead.</summary>
    public const string ErrorParameter = "error";

    /// <summary>The <see cref="ErrorParameter"/> value for an address a password account already owns.</summary>
    public const string EmailInUseError = "email_in_use";
}

/// <summary>Body of <see cref="ExternalSignIn.ExchangePath"/>.</summary>
/// <param name="Code">The one-time code the callback delivered.</param>
/// <param name="Verifier">The PKCE verifier whose SHA-256 the app sent when it started.</param>
public record ExternalExchangeRequest(string Code, string Verifier);
