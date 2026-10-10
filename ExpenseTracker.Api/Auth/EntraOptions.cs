namespace ExpenseTracker.Api.Auth;

/// <summary>
/// Settings for "Sign in with Microsoft". Bound from the <c>Entra</c> configuration section.
/// </summary>
/// <remarks>
/// Optional as a whole: without a client id and secret the feature is simply off, so local
/// development, the test suite and any deployment that has not registered an app keep working
/// on passwords alone. The secret never lives in appsettings.json - same rule as the JWT key.
/// </remarks>
public sealed class EntraOptions
{
    public const string Section = "Entra";

    /// <summary>
    /// <c>common</c> accepts work and school accounts from any tenant plus personal Microsoft
    /// accounts, which is what a public expense tracker wants. Use a tenant id to restrict
    /// sign-in to one organisation.
    /// </summary>
    public const string DefaultAuthority = "https://login.microsoftonline.com/common/v2.0";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string Authority { get; set; } = DefaultAuthority;

    public bool Enabled => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    /// <summary>
    /// Whether the authority is one of the shared endpoints whose discovery document names a
    /// placeholder issuer (<c>{tenantid}</c>) rather than a real one.
    /// </summary>
    public bool IsMultiTenant =>
        Authority.Contains("/common/", StringComparison.OrdinalIgnoreCase)
        || Authority.Contains("/organizations/", StringComparison.OrdinalIgnoreCase)
        || Authority.Contains("/consumers/", StringComparison.OrdinalIgnoreCase);
}
