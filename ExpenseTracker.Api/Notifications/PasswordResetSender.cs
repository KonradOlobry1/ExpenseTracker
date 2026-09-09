namespace ExpenseTracker.Api.Notifications;

/// <summary>
/// Delivers a password reset token to the address that asked for it.
/// </summary>
/// <remarks>
/// A seam rather than a concrete mail client, because this token is the entire security
/// boundary of the reset flow: whoever reads it can take the account. Putting delivery behind
/// an interface keeps the controller structurally unable to return the token in its own
/// response, which would hand it to whoever made the request rather than to whoever owns the
/// mailbox — and those are the same party only when nothing is wrong.
/// </remarks>
public interface IPasswordResetSender
{
    Task SendAsync(string email, string token, CancellationToken ct = default);
}

/// <summary>
/// The stand-in registered until a real email provider is configured.
/// </summary>
/// <remarks>
/// In Development it writes the token to the log, so the flow can be exercised end to end
/// without a mail server. Everywhere else it deliberately does not: a live reset token in a log
/// file is a credential sitting in plain text, in a place that outlives its expiry and is
/// readable by anyone with log access — which is a wider audience than the mailbox it was meant
/// for.
///
/// So outside Development this delivers nothing and logs an error. Password reset does not work
/// in production until a real sender replaces this, and that is the intended behaviour rather
/// than an oversight: failing loudly beats mailing tokens into a log.
///
/// The endpoint answers 200 either way. It cannot report delivery without also reporting
/// whether the address is registered.
/// </remarks>
public class LoggingPasswordResetSender(
    IHostEnvironment environment,
    ILogger<LoggingPasswordResetSender> logger) : IPasswordResetSender
{
    public Task SendAsync(string email, string token, CancellationToken ct = default)
    {
        if (environment.IsDevelopment())
            logger.LogWarning(
                "Development only — password reset token for {Email}: {Token}", email, token);
        else
            logger.LogError(
                "A password reset was requested for {Email}, but no IPasswordResetSender is "
                + "configured, so nothing was delivered. Register a real sender.", email);

        return Task.CompletedTask;
    }
}
