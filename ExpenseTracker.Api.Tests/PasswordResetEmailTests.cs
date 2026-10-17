using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using ExpenseTracker.Api.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static ExpenseTracker.Api.Tests.TestClient;

namespace ExpenseTracker.Api.Tests;

public sealed record SentMail(string To, string Subject, string Html, string Text);

/// <summary>Records mail instead of sending it, and can be told to fail.</summary>
public sealed class FakeMailTransport : IMailTransport
{
    private readonly ConcurrentQueue<SentMail> _sent = new();

    public bool Fail { get; set; }
    public IReadOnlyCollection<SentMail> Sent => _sent;

    public Task SendAsync(string to, string subject, string htmlBody, string textBody, CancellationToken ct)
    {
        if (Fail) throw new InvalidOperationException("Mail provider is down.");

        _sent.Enqueue(new SentMail(to, subject, htmlBody, textBody));
        return Task.CompletedTask;
    }
}

/// <summary>The API with email configured and the provider replaced by <see cref="FakeMailTransport"/>.</summary>
public class MailFactory : ApiFactory
{
    public const string PublicBaseUrl = "https://app.example.test";

    public FakeMailTransport Mail { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("Email:Endpoint", "https://example.communication.azure.com");
        builder.UseSetting("Email:SenderAddress", "DoNotReply@example.test");
        builder.UseSetting("Email:PublicBaseUrl", PublicBaseUrl);

        builder.ConfigureServices(services => services.AddSingleton<IMailTransport>(Mail));
    }
}

/// <summary>
/// The reset email: that it reaches the right person, that its link really resets the
/// password, and that a mail outage never changes what the endpoint tells a stranger.
/// </summary>
/// <remarks>
/// Delete these and password reset can quietly stop working in production - the controller
/// tests use a capturing sender and never see the message a user would actually receive - or
/// start pointing links at whatever Host header the caller sent.
/// </remarks>
public class PasswordResetEmailTests(MailFactory factory) : IClassFixture<MailFactory>
{
    private static string TokenFrom(SentMail mail)
    {
        var start = mail.Text.IndexOf("token=", StringComparison.Ordinal) + "token=".Length;
        var end = mail.Text.IndexOfAny(['\n', ' '], start);
        return Uri.UnescapeDataString(mail.Text[start..(end < 0 ? mail.Text.Length : end)]);
    }

    private SentMail? MailTo(string email) => factory.Mail.Sent.LastOrDefault(m => m.To == email);

    [Fact]
    public async Task A_registered_address_receives_a_link_on_the_configured_site()
    {
        var email = $"mail-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAsync(email);

        await factory.CreateClient().PostAsJsonAsync("/api/auth/forgot-password", new { Email = email });

        var mail = MailTo(email);
        Assert.NotNull(mail);
        Assert.Contains($"{MailFactory.PublicBaseUrl}/account/reset-password?token=", mail.Text);
        Assert.Contains($"{MailFactory.PublicBaseUrl}/account/reset-password?token=", mail.Html);
    }

    [Fact]
    public async Task The_link_in_the_email_actually_resets_the_password()
    {
        var email = $"mail-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAsync(email);
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = email });

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { Token = TokenFrom(MailTo(email)!), NewPassword = "N3wPassw0rd!" });
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = "N3wPassw0rd!" });

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task A_forged_host_header_cannot_redirect_the_link()
    {
        // The endpoint is anonymous. If links were built from the request, anyone could ask for
        // a reset for somebody else's address with their own Host and have the token delivered
        // to a server they control.
        var email = $"mail-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAsync(email);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Host = "evil.example";
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "evil.example");

        await client.PostAsJsonAsync("/api/auth/forgot-password", new { Email = email });

        var mail = MailTo(email)!;
        Assert.DoesNotContain("evil.example", mail.Text);
        Assert.DoesNotContain("evil.example", mail.Html);
    }

    [Fact]
    public async Task An_unknown_address_is_told_the_same_thing_and_nothing_is_sent()
    {
        var email = $"nobody-{Guid.NewGuid():N}@test.local";

        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/auth/forgot-password", new { Email = email });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(MailTo(email));
    }

    [Fact]
    public async Task A_mail_outage_does_not_change_the_answer()
    {
        // Throwing here would make a registered address fail with a 500 while an unregistered
        // one succeeds - exactly the difference the uniform answer exists to hide.
        var email = $"mail-{Guid.NewGuid():N}@test.local";
        await factory.RegisterAsync(email);
        factory.Mail.Fail = true;

        try
        {
            var response = await factory.CreateClient()
                .PostAsJsonAsync("/api/auth/forgot-password", new { Email = email });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            factory.Mail.Fail = false;
        }
    }
}

public class MailPasswordResetSenderTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception) + exception);
    }

    private static EmailOptions Options(string baseUrl = "https://app.example.test") => new()
    {
        Endpoint = "https://example.communication.azure.com",
        SenderAddress = "DoNotReply@example.test",
        PublicBaseUrl = baseUrl,
    };

    [Fact]
    public async Task The_token_is_escaped_into_the_link_and_a_trailing_slash_is_harmless()
    {
        var transport = new FakeMailTransport();
        var sender = new MailPasswordResetSender(transport, Options("https://app.example.test/"),
            new CapturingLogger<MailPasswordResetSender>());

        await sender.SendAsync("a@test.local", "tok en&x=1", default);

        var mail = Assert.Single(transport.Sent);
        Assert.Contains("https://app.example.test/account/reset-password?token=tok%20en%26x%3D1", mail.Text);
        Assert.DoesNotContain("//account", mail.Text);
        Assert.DoesNotContain("tok en", mail.Subject);
    }

    [Fact]
    public async Task A_delivery_failure_never_puts_the_token_or_link_in_the_log()
    {
        var logger = new CapturingLogger<MailPasswordResetSender>();
        var sender = new MailPasswordResetSender(new FakeMailTransport { Fail = true }, Options(), logger);

        await sender.SendAsync("a@test.local", "SECRET-TOKEN", default);

        Assert.NotEmpty(logger.Messages);
        Assert.All(logger.Messages, m => Assert.DoesNotContain("SECRET-TOKEN", m));
    }

    [Fact]
    public void Half_configured_email_fails_startup_instead_of_silently_never_sending()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new EmailOptions { Endpoint = "https://x.communication.azure.com", PublicBaseUrl = "https://a.test" }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new EmailOptions { Endpoint = "https://x.communication.azure.com", SenderAddress = "a@b.test" }.Validate());
        Assert.Throws<InvalidOperationException>(() =>
            new EmailOptions
            {
                Endpoint = "https://x.communication.azure.com", SenderAddress = "a@b.test",
                PublicBaseUrl = "http://app.example.test"   // links carrying a token must be https
            }.Validate());
    }

    [Fact]
    public void Unconfigured_email_is_simply_off_and_localhost_may_use_http()
    {
        Assert.False(new EmailOptions().Enabled);
        new EmailOptions().Validate();

        new EmailOptions
        {
            Endpoint = "https://x.communication.azure.com", SenderAddress = "a@b.test",
            PublicBaseUrl = "http://localhost:5000"
        }.Validate();
    }
}
