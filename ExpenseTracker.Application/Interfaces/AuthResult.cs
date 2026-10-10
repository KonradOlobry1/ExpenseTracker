namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// Why <see cref="AuthResult.Succeeded"/> is false. A caller branches on this; the message
/// shown to the user comes from <c>Translations</c>, keyed by the reason — not from any text
/// carried on this type, since every UI-facing string already goes through localization.
/// </summary>
public enum AuthFailureReason
{
    InvalidCredentials,
    AccountLocked,
    NetworkError,
    ServerError,

    /// <summary>The user closed the Microsoft sign-in without finishing. Not an error to show.</summary>
    Cancelled,

    /// <summary>A password account already uses the Microsoft account's email; they are not merged.</summary>
    EmailInUse,
}

/// <summary>
/// Replaces a bare <c>bool</c> on <see cref="IAuthService.LoginAsync"/> and
/// <see cref="IAuthService.RegisterAsync"/>. A bare bool could not distinguish a wrong
/// password from an unreachable server, so every failure looked identical to the UI — which
/// is why the login page used to show one generic message ("check the server URL") for every
/// possible cause, including ones that URL had nothing to do with.
/// </summary>
public readonly record struct AuthResult(bool Succeeded, AuthFailureReason? Failure = null)
{
    public static AuthResult Success() => new(true);
    public static AuthResult Fail(AuthFailureReason reason) => new(false, reason);
}
