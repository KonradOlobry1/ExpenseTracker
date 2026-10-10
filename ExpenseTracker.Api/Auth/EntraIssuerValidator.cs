using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace ExpenseTracker.Api.Auth;

/// <summary>
/// Issuer check for the multi-tenant authorities.
/// </summary>
/// <remarks>
/// The <c>common</c> discovery document advertises a templated issuer
/// (<c>.../{tenantid}/v2.0</c>), so the stock validator rejects every real token. Switching
/// validation off would accept a token from anyone; instead the issuer must be the v2 issuer
/// of the very tenant the token says it came from, which is what the template means.
/// </remarks>
public static class EntraIssuerValidator
{
    private const string Prefix = "https://login.microsoftonline.com/";
    private const string Suffix = "/v2.0";

    public static string Validate(string issuer, SecurityToken token, TokenValidationParameters _)
    {
        if (token is JsonWebToken jwt
            && jwt.TryGetPayloadValue<string>("tid", out var tenant)
            && !string.IsNullOrWhiteSpace(tenant)
            && string.Equals(issuer, $"{Prefix}{tenant}{Suffix}", StringComparison.OrdinalIgnoreCase))
            return issuer;

        throw new SecurityTokenInvalidIssuerException("Issuer does not match the token's tenant.")
        {
            InvalidIssuer = issuer
        };
    }
}
