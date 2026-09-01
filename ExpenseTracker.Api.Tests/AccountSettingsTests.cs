using System.Net.Http.Json;
using static ExpenseTracker.Api.Tests.TestClient;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// Currency, language and theme live on the account so they follow the user between devices.
/// They ride along with the ordinary sync payload and resolve by the client's own clock, the
/// same way rows do.
/// </summary>
public class AccountSettingsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static PushSettings Settings(
        string currency = "PLN", string language = "pl", bool dark = true, int year = 2026)
        => new(currency, language, dark, new DateTime(year, 6, 1, 0, 0, 0, DateTimeKind.Utc));

    private static Task<HttpResponseMessage> PushAsync(HttpClient client, PushSettings settings)
        => client.PostAsJsonAsync("/api/sync/push", new PushPayload(Settings: settings));

    private static async Task<PushSettings?> PullSettingsAsync(HttpClient client)
        => (await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>())!.Settings;

    [Fact]
    public async Task A_new_account_starts_on_the_defaults()
    {
        var client = await factory.RegisterAsync();

        var settings = await PullSettingsAsync(client);

        Assert.Equal("USD", settings!.Currency);
        Assert.Equal("en", settings.Language);
        Assert.False(settings.IsDarkMode);
    }

    [Fact]
    public async Task Pushed_settings_come_back_on_the_next_pull()
    {
        // The whole point: choose Polish and złoty on the phone, sign in on the desktop, get
        // Polish and złoty.
        var client = await factory.RegisterAsync();

        await PushAsync(client, Settings());
        var settings = await PullSettingsAsync(client);

        Assert.Equal("PLN", settings!.Currency);
        Assert.Equal("pl", settings.Language);
        Assert.True(settings.IsDarkMode);
    }

    [Fact]
    public async Task An_older_change_does_not_overwrite_a_newer_one()
    {
        // Two devices edited offline. The one that syncs second is not automatically right.
        var client = await factory.RegisterAsync();

        await PushAsync(client, Settings("EUR", "en", false, year: 2026));
        await PushAsync(client, Settings("PLN", "pl", true, year: 2025));

        var settings = await PullSettingsAsync(client);

        Assert.Equal("EUR", settings!.Currency);
    }

    [Fact]
    public async Task A_newer_change_does_overwrite_an_older_one()
    {
        var client = await factory.RegisterAsync();

        await PushAsync(client, Settings("EUR", "en", false, year: 2025));
        await PushAsync(client, Settings("PLN", "pl", true, year: 2026));

        var settings = await PullSettingsAsync(client);

        Assert.Equal("PLN", settings!.Currency);
    }

    [Fact]
    public async Task A_device_that_has_never_chosen_anything_cannot_clobber_the_account()
    {
        // A fresh install sends DateTime.MinValue, which must lose to anything stored —
        // otherwise reinstalling the app would reset the account to USD/English.
        var client = await factory.RegisterAsync();

        await PushAsync(client, Settings("PLN", "pl", true));
        await PushAsync(client, new PushSettings("USD", "en", false, DateTime.MinValue));

        var settings = await PullSettingsAsync(client);

        Assert.Equal("PLN", settings!.Currency);
    }

    [Fact]
    public async Task Settings_are_not_shared_between_accounts()
    {
        var alice = await factory.RegisterAsync();
        var bob = await factory.RegisterAsync();

        await PushAsync(alice, Settings("PLN", "pl", true));

        var bobs = await PullSettingsAsync(bob);

        Assert.Equal("USD", bobs!.Currency);
    }

    [Fact]
    public async Task A_push_without_settings_leaves_them_alone()
    {
        // Rows and settings travel together, but a client may legitimately send only rows.
        var client = await factory.RegisterAsync();

        await PushAsync(client, Settings("PLN", "pl", true));
        await client.PostAsJsonAsync("/api/sync/push", new PushPayload());

        var settings = await PullSettingsAsync(client);

        Assert.Equal("PLN", settings!.Currency);
    }

    [Fact]
    public async Task Settings_ignore_the_since_filter()
    {
        // Rows are filtered by `since`; settings are not. A device syncing incrementally must
        // still learn about a currency change it has never seen.
        var client = await factory.RegisterAsync();
        await PushAsync(client, Settings("PLN", "pl", true));

        var future = DateTime.UtcNow.AddYears(1);
        var pulled = await (await client.GetAsync($"/api/sync/pull?since={future:O}"))
            .ReadAsync<PullResponse>();

        Assert.Equal("PLN", pulled!.Settings!.Currency);
    }
}
