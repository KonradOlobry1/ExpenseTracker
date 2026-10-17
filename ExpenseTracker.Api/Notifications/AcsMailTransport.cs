using Azure;
using Azure.Communication.Email;
using Azure.Identity;

namespace ExpenseTracker.Api.Notifications;

/// <summary>
/// Sends through Azure Communication Services Email.
/// </summary>
/// <remarks>
/// With <see cref="EmailOptions.Endpoint"/> it signs in as the App Service's managed identity
/// (and as the developer's own login locally), so no key exists to leak. The identity needs a
/// role on the Communication Services resource that allows sending email.
///
/// Returns once Azure has accepted the message rather than waiting for delivery, so a slow mail
/// provider cannot make the forgot-password page slow - which would also be visible from
/// outside as a difference between registered and unregistered addresses.
/// </remarks>
public sealed class AcsMailTransport : IMailTransport
{
    private readonly EmailClient _client;
    private readonly string _sender;

    public AcsMailTransport(EmailOptions options)
    {
        _sender = options.SenderAddress!;
        _client = !string.IsNullOrWhiteSpace(options.ConnectionString)
            ? new EmailClient(options.ConnectionString)
            : new EmailClient(new Uri(options.Endpoint!), new DefaultAzureCredential());
    }

    public async Task SendAsync(
        string to, string subject, string htmlBody, string textBody, CancellationToken ct)
    {
        var message = new EmailMessage(_sender, to, new EmailContent(subject)
        {
            Html = htmlBody,
            PlainText = textBody,
        });

        await _client.SendAsync(WaitUntil.Started, message, ct);
    }
}
