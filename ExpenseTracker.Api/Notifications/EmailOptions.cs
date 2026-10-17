namespace ExpenseTracker.Api.Notifications;

/// <summary>
/// Settings for sending email through Azure Communication Services. Bound from the
/// <c>Email</c> configuration section.
/// </summary>
/// <remarks>
/// Optional as a whole: with neither <see cref="Endpoint"/> nor <see cref="ConnectionString"/>
/// the app keeps its stand-in sender and password reset delivers nothing outside Development.
///
/// <see cref="Endpoint"/> is the way to run in Azure: the App Service's managed identity signs
/// in, so there is no key to store. <see cref="ConnectionString"/> carries an access key and
/// exists only as a fallback; if it is used it belongs in Key Vault, never in appsettings.json.
/// </remarks>
public sealed class EmailOptions
{
    public const string Section = "Email";

    /// <summary>The resource's address, e.g. <c>https://myacs.communication.azure.com</c>.</summary>
    public string? Endpoint { get; set; }

    /// <summary>Fallback to <see cref="Endpoint"/>: a connection string that includes an access key.</summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// A sender address on a domain connected to the resource, e.g.
    /// <c>DoNotReply@&lt;guid&gt;.azurecomm.net</c> for the free Azure-managed domain.
    /// </summary>
    public string? SenderAddress { get; set; }

    /// <summary>
    /// The address links in emails point at, e.g. <c>https://expensetracker.example.com</c>.
    /// </summary>
    /// <remarks>
    /// Configured rather than read from the request on purpose. The reset endpoint is
    /// anonymous, so a request-derived address would let anyone send a forgot-password request
    /// with a forged Host header and have the victim's reset link - token included - point at a
    /// server of their choosing.
    /// </remarks>
    public string? PublicBaseUrl { get; set; }

    public bool Enabled =>
        !string.IsNullOrWhiteSpace(Endpoint) || !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>
    /// Fails startup on a half-configured setup. Sending mail is the only thing standing
    /// between a user and a locked-out account, and "configured but silently broken" is worse
    /// than "not configured" because nothing says so.
    /// </summary>
    public void Validate()
    {
        if (!Enabled) return;

        if (string.IsNullOrWhiteSpace(SenderAddress))
            throw new InvalidOperationException(
                "Email is configured but Email:SenderAddress is not. Set it to a sender on a domain "
                + "connected to the Communication Services resource.");

        if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && !baseUri.IsLoopback))
            throw new InvalidOperationException(
                "Email is configured but Email:PublicBaseUrl is missing or not an https address. "
                + "Reset links are built from it, so it must be the site's real public address.");

        if (string.IsNullOrWhiteSpace(ConnectionString)
            && !Uri.TryCreate(Endpoint, UriKind.Absolute, out _))
            throw new InvalidOperationException("Email:Endpoint is not a valid address.");
    }
}
