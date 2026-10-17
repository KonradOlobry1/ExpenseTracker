using System.Net;

namespace ExpenseTracker.Api.Notifications;

/// <summary>
/// The one thing the sender needs from an email provider. Keeps Azure out of the code that
/// decides what to send, so that part can be tested without a mail service.
/// </summary>
public interface IMailTransport
{
    Task SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken ct);
}

/// <summary>
/// Emails the reset link to the address that asked for it.
/// </summary>
/// <remarks>
/// A failure to deliver is logged and swallowed, never thrown. The reset endpoints answer the
/// same way whether or not the address has an account (see <see cref="LoggingPasswordResetSender"/>);
/// an exception here would turn a mail outage into a 500 for registered addresses only, which is
/// the membership oracle that uniform answer exists to prevent. The cost is that a user whose
/// mail never arrives is not told - they can ask again, and the failure is in the log.
///
/// Neither the token nor the link is ever logged: either one is the credential.
/// </remarks>
public sealed class MailPasswordResetSender(
    IMailTransport transport,
    EmailOptions options,
    ILogger<MailPasswordResetSender> logger) : IPasswordResetSender
{
    internal const string Subject = "Reset your ExpenseTracker password";

    public async Task SendAsync(string email, string token, CancellationToken ct = default)
    {
        var link = ResetLink(options.PublicBaseUrl!, token);

        try
        {
            await transport.SendAsync(email, Subject, Html(link), Text(link), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not deliver a password reset email to {Email}.", email);
        }
    }

    internal static string ResetLink(string baseUrl, string token)
        => $"{baseUrl.TrimEnd('/')}/account/reset-password?token={Uri.EscapeDataString(token)}";

    private static string Text(string link) =>
        "Someone asked to reset the password for your ExpenseTracker account.\n\n"
        + $"Open this link to choose a new password:\n{link}\n\n"
        + "The link works once and expires in one hour. If you did not ask for this, ignore this "
        + "email - your password has not changed.";

    private static string Html(string link)
    {
        var safe = WebUtility.HtmlEncode(link);
        return "<p>Someone asked to reset the password for your ExpenseTracker account.</p>"
             + $"<p><a href=\"{safe}\">Choose a new password</a></p>"
             + $"<p>Or paste this address into your browser:<br>{safe}</p>"
             + "<p>The link works once and expires in one hour. If you did not ask for this, ignore "
             + "this email - your password has not changed.</p>";
    }
}
