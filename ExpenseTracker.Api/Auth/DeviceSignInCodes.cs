using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace ExpenseTracker.Api.Auth;

/// <summary>
/// The one-time code the browser hands the app at the end of a Microsoft sign-in, bound to a
/// PKCE challenge so the code alone is worthless.
/// </summary>
/// <remarks>
/// The code travels through a custom URL scheme, which any other installed app can also claim,
/// so it has to be safe to leak. The app generates a random verifier, sends only its SHA-256
/// when it starts, and must present the verifier to redeem - an app that merely intercepts the
/// redirect has the code and not the verifier.
///
/// Stateless on purpose: the code is a time-limited Data Protection payload (the key ring is
/// already in the database, so every instance agrees), which avoids a table and a SQL Server
/// migration for something that lives two minutes. The price is that a code is not strictly
/// single-use - it can be redeemed again inside its lifetime by whoever holds the verifier, and
/// that is only ever the device that started the flow.
/// </remarks>
public sealed class DeviceSignInCodes(IDataProtectionProvider provider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("ExpenseTracker.DeviceSignInCode").ToTimeLimitedDataProtector();

    /// <summary>A PKCE S256 challenge is 32 bytes, base64url-encoded: exactly 43 characters.</summary>
    public static bool IsValidChallenge(string? challenge) =>
        challenge is { Length: 43 } && challenge.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public string Issue(string userId, string challenge) =>
        _protector.Protect($"{userId}|{challenge}", Lifetime);

    /// <summary>The account the code was issued for, or null if it is forged, expired or unbound.</summary>
    public string? Redeem(string code, string verifier)
    {
        string payload;
        try
        {
            payload = _protector.Unprotect(code);
        }
        catch (CryptographicException)
        {
            // Tampered, expired and from-another-key-ring all land here, and none of them is
            // worth distinguishing for a caller who is not entitled to the account.
            return null;
        }

        var split = payload.IndexOf('|');
        if (split <= 0) return null;

        var expected = Encoding.ASCII.GetBytes(payload[(split + 1)..]);
        var actual = Encoding.ASCII.GetBytes(
            WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));

        return CryptographicOperations.FixedTimeEquals(expected, actual) ? payload[..split] : null;
    }
}
