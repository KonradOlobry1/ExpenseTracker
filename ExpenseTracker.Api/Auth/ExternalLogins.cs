using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Api.Auth;

public enum ExternalLoginFailure
{
    /// <summary>The provider released no usable email address.</summary>
    NoEmail,

    /// <summary>A password account already owns this address and was deliberately not linked.</summary>
    EmailInUse,

    /// <summary>The account is locked out.</summary>
    Locked,

    /// <summary>Anything else - the account could not be created.</summary>
    Failed,
}

public readonly record struct ExternalLoginResult(AppUser? User, ExternalLoginFailure? Failure)
{
    public bool Succeeded => User is not null;
    public static ExternalLoginResult Ok(AppUser user) => new(user, null);
    public static ExternalLoginResult Fail(ExternalLoginFailure why) => new(null, why);
}

/// <summary>
/// Turns a verified Microsoft identity into a local account - the one place that decision is
/// made, shared by the browser and the device flow so they cannot diverge.
/// </summary>
/// <remarks>
/// A returning identity is found by the provider's own subject id, never by email. An
/// identity new to us becomes a new, password-less account - and if its email already belongs
/// to a password account it is refused, not merged. Entra's <c>email</c> claim is not proof of
/// ownership (a tenant admin can set any address on a user), so linking on a matching email
/// would let anyone with their own tenant take over the account of whoever they name.
///
/// The cost of that is that a person who registered with a password and later clicks the
/// Microsoft button gets a message instead of their account. Linking from inside a signed-in
/// session is the safe way to offer it, and does not exist yet.
/// </remarks>
public sealed class ExternalLogins(UserManager<AppUser> users, ApiDbContext db)
{
    public async Task<ExternalLoginResult> ResolveAsync(ExternalLoginInfo info, CancellationToken ct)
    {
        var existing = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (existing is not null)
            return await users.IsLockedOutAsync(existing)
                ? ExternalLoginResult.Fail(ExternalLoginFailure.Locked)
                : ExternalLoginResult.Ok(existing);

        var email = EmailFrom(info.Principal);
        if (email is null)
            return ExternalLoginResult.Fail(ExternalLoginFailure.NoEmail);

        if (await users.FindByEmailAsync(email) is not null)
            return ExternalLoginResult.Fail(ExternalLoginFailure.EmailInUse);

        var user = new AppUser { UserName = email, Email = email };

        // No password: the account can only be entered through its Microsoft identity until
        // its owner proves control of the address through password reset.
        if (!(await users.CreateAsync(user)).Succeeded)
            return ExternalLoginResult.Fail(ExternalLoginFailure.Failed);

        if (!(await users.AddLoginAsync(user, info)).Succeeded)
        {
            // An account that cannot be signed into again would just squat on the address.
            await users.DeleteAsync(user);
            return ExternalLoginResult.Fail(ExternalLoginFailure.Failed);
        }

        // Same as both password registration paths: no categories means no expense can exist.
        await DefaultCategories.EnsureForUserAsync(db, user.Id, ct);

        return ExternalLoginResult.Ok(user);
    }

    /// <summary>
    /// <c>email</c> is optional in Entra's id tokens, and personal accounts often omit it;
    /// <c>preferred_username</c> is the sign-in name, which is an address for most accounts but
    /// not guaranteed to be one, so it is only trusted when it looks like one.
    /// </summary>
    private static string? EmailFrom(ClaimsPrincipal principal)
    {
        foreach (var candidate in new[]
        {
            principal.FindFirstValue(ClaimTypes.Email),
            principal.FindFirstValue("preferred_username"),
        })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && new EmailAddressAttribute().IsValid(candidate))
                return candidate.Trim();
        }

        return null;
    }
}
