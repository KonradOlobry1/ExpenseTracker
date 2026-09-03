using System.Net;
using ExpenseTracker.Contracts;
using ExpenseTracker.Application.Interfaces;
using ExpenseTracker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using static ExpenseTracker.Infrastructure.Tests.SyncHarness;

namespace ExpenseTracker.Infrastructure.Tests;

/// <summary>
/// The device half of sync. The server half has its own suite; these cover what happens on
/// the phone when a delta arrives, which is where a mistake silently corrupts local data.
/// </summary>
public class PulledDataTests
{
    [Fact]
    public async Task A_pulled_row_is_inserted()
    {
        using var h = new SyncHarness();
        var categoryId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        h.Api.PullResponse = new SyncPullResponse(
            [Expense(expenseId, categoryId, 42.50m, "From the cloud")],
            null, null,
            [Category(categoryId)],
            DateTime.UtcNow, null);

        await h.SyncOrExplainAsync();

        await using var db = h.NewDbContext();
        var expense = await db.Expenses.SingleAsync();
        Assert.Equal(expenseId, expense.SyncId);
        Assert.Equal(42.50m, expense.Amount);
    }

    [Fact]
    public async Task A_pulled_category_may_share_a_name_with_a_local_one()
    {
        // The cloud does not constrain category names — that index was removed when it made
        // pushes fail — so a category created in the browser can arrive here named "Food"
        // while the built-in "Food" already exists under a different SyncId. The device held a
        // unique index on Name, which failed the entire pull. Sync logs its errors rather than
        // surfacing them, so the symptom was a phone that quietly stopped syncing altogether.
        using var h = new SyncHarness();

        h.Api.PullResponse = new SyncPullResponse(
            null, null, null, [Category(Guid.NewGuid(), "Food")], DateTime.UtcNow, null);

        await h.SyncOrExplainAsync();

        await using var db = h.NewDbContext();
        Assert.Equal(2, await db.Categories.CountAsync(c => c.Name == "Food"));
    }

    [Fact]
    public async Task A_pulled_tombstone_deletes_the_local_row_rather_than_inserting_it()
    {
        // The defect this guards: UpsertPulledData inserts anything it cannot find locally.
        // A tombstone for a row this device already deleted must not come back as a new row.
        using var h = new SyncHarness();
        var categoryId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        h.Api.PullResponse = new SyncPullResponse(
            [Expense(expenseId, categoryId, 10m, "Doomed")], null, null,
            [Category(categoryId)], DateTime.UtcNow, null);
        await h.Sync.SyncAsync();

        h.Api.PullResponse = new SyncPullResponse(
            [Expense(expenseId, categoryId, 10m, "Doomed", isDeleted: true)], null, null,
            null, DateTime.UtcNow, null);
        await h.Sync.SyncAsync();

        await using var db = h.NewDbContext();
        Assert.Empty(await db.Expenses.ToListAsync());                       // hidden by the filter
        Assert.True((await db.Expenses.IgnoreQueryFilters().SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task A_locally_deleted_row_is_pushed_as_a_tombstone()
    {
        // A delete that never leaves the device would be undone by the next pull.
        using var h = new SyncHarness();
        var categoryId = Guid.NewGuid();

        h.Api.PullResponse = new SyncPullResponse(
            null, null, null, [Category(categoryId)], DateTime.UtcNow, null);
        await h.Sync.SyncAsync();

        await using (var db = h.NewDbContext())
        {
            var category = await db.Categories.SingleAsync(c => c.SyncId == categoryId);
            db.Expenses.Add(new Expense
            {
                SyncId = Guid.NewGuid(), Description = "Deleted locally", Amount = 5m,
                Date = DateTime.UtcNow, CategoryId = category.Id,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsDeleted = true
            });
            await db.SaveChangesAsync();
        }

        await h.Sync.SyncAsync();

        var pushed = h.Api.Pushes[^1].Expenses!;
        Assert.True(Assert.Single(pushed).IsDeleted);
    }
}

public class SyncSettingsTests
{
    [Fact]
    public async Task A_device_that_has_never_chosen_anything_pushes_the_minimum_stamp()
    {
        // So it always loses to whatever the account holds: reinstalling must not reset it.
        using var h = new SyncHarness();

        await h.Sync.SyncAsync();

        Assert.Equal(DateTime.MinValue, h.Api.Pushes[^1].Settings!.UpdatedAt);
    }

    [Fact]
    public async Task Newer_settings_from_the_account_are_adopted()
    {
        using var h = new SyncHarness();
        h.Api.PullResponse = new SyncPullResponse(
            null, null, null, null, DateTime.UtcNow, SettingsDto("PLN", "pl", dark: true));

        await h.Sync.SyncAsync();

        Assert.Equal("PLN", h.Currency.Selected.Code);
        Assert.Equal("pl", h.Localization.CurrentLanguage);
        Assert.True(h.Theme.IsDarkMode);
    }

    [Fact]
    public async Task Older_settings_from_the_account_are_ignored()
    {
        using var h = new SyncHarness();
        h.Currency.SetCurrency("EUR");                    // a local choice, stamped now

        h.Api.PullResponse = new SyncPullResponse(
            null, null, null, null, DateTime.UtcNow,
            SettingsDto("PLN", "pl", updatedAt: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));

        await h.Sync.SyncAsync();

        Assert.Equal("EUR", h.Currency.Selected.Code);
    }

    [Fact]
    public async Task Adopting_settings_keeps_the_accounts_stamp_rather_than_restamping_them()
    {
        // The setters mark preferences as locally edited. Left alone, the next sync would push
        // these values back as if this device had chosen them, and they would beat a genuinely
        // newer edit made elsewhere purely by having been re-stamped.
        using var h = new SyncHarness();
        var accountStamp = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        h.Api.PullResponse = new SyncPullResponse(
            null, null, null, null, DateTime.UtcNow, SettingsDto(updatedAt: accountStamp));

        await h.Sync.SyncAsync();

        Assert.Equal(accountStamp, h.Settings.UpdatedAt);
    }

    [Fact]
    public async Task An_unknown_language_from_the_account_does_not_take_the_app_down()
    {
        // CreateSpecificCulture throws on a code it does not recognise, and these values now
        // arrive from another device rather than only from this build's own list.
        using var h = new SyncHarness();
        h.Api.PullResponse = new SyncPullResponse(
            null, null, null, null, DateTime.UtcNow, SettingsDto("PLN", "kl"));

        Assert.True((await h.Sync.SyncAsync()).Succeeded);
        Assert.Equal("en", h.Localization.CurrentLanguage);
    }
}

public class SyncFailureTests
{
    [Fact]
    public async Task A_failed_push_leaves_the_sync_marker_untouched()
    {
        // The delta is computed from this marker. Advancing it after a failed push would make
        // the next sync skip exactly the rows that never arrived — a silent data loss.
        using var h = new SyncHarness();
        h.Api.PushStatus = HttpStatusCode.InternalServerError;

        var result = await h.Sync.SyncAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(SyncFailureReason.ServerError, result.Failure);
        Assert.Null(h.Sync.LastSyncTime);
    }

    [Fact]
    public async Task A_successful_sync_advances_the_marker()
    {
        using var h = new SyncHarness();

        Assert.True((await h.Sync.SyncAsync()).Succeeded);
        Assert.NotNull(h.Sync.LastSyncTime);
    }

    [Fact]
    public async Task A_second_sync_asks_only_for_what_changed()
    {
        using var h = new SyncHarness();

        await h.Sync.SyncAsync();
        await h.Sync.SyncAsync();

        Assert.DoesNotContain("since=", h.Api.PullUrls[0]);
        Assert.Contains("since=", h.Api.PullUrls[1]);
    }

    [Fact]
    public async Task Sync_does_nothing_when_signed_out()
    {
        using var h = new SyncHarness(signedIn: false);

        var result = await h.Sync.SyncAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(SyncFailureReason.NotSignedIn, result.Failure);
        Assert.Empty(h.Api.Pushes);
    }
}

/// <summary>
/// The app gate asks whether this device belongs to an account; sync asks whether the token
/// is still valid. Conflating the two would either let a stranger open the app or lock the
/// owner out of their own offline data.
/// </summary>
public class SessionTests
{
    [Fact]
    public async Task A_fresh_install_has_no_session()
    {
        using var h = new SyncHarness(signedIn: false);

        Assert.False(await h.Auth.HasStoredSessionAsync());
    }

    [Fact]
    public async Task Signing_in_creates_a_session()
    {
        using var h = new SyncHarness();

        Assert.True(await h.Auth.HasStoredSessionAsync());
    }

    [Fact]
    public async Task An_expired_token_still_counts_as_a_session()
    {
        // The whole point of the distinction. The app opens and the local replica stays
        // readable regardless of whether a silent refresh is even possible — no refresh
        // token here, so IsLoggedInAsync has no way to renew and sync stops until the user
        // signs in again. See SilentRefreshTests for the case where a refresh token exists.
        using var h = new SyncHarness(signedIn: false);
        h.SignIn(expiry: DateTime.UtcNow.AddHours(-1), refreshToken: null);

        Assert.True(await h.Auth.HasStoredSessionAsync());
        Assert.False(await h.Auth.IsLoggedInAsync());
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        using var h = new SyncHarness();

        await h.Auth.LogoutAsync();

        Assert.False(await h.Auth.HasStoredSessionAsync());
    }
}

public class LogoutTests
{
    [Fact]
    public async Task Signing_out_clears_the_replica_and_both_markers()
    {
        // Leaving them behind meant signing in as somebody else kept the previous account's
        // rows on screen — and the next edit to one pushed it into the new account.
        using var h = new SyncHarness();
        var categoryId = Guid.NewGuid();

        h.Api.PullResponse = new SyncPullResponse(
            [Expense(Guid.NewGuid(), categoryId, 10m, "Previous account")], null, null,
            [Category(categoryId, "Custom")], DateTime.UtcNow, SettingsDto());
        await h.Sync.SyncAsync();

        await h.Auth.LogoutAsync();

        await using var db = h.NewDbContext();
        Assert.Empty(await db.Expenses.IgnoreQueryFilters().ToListAsync());
        Assert.Null(h.Sync.LastSyncTime);
        Assert.Equal(DateTime.MinValue, h.Settings.UpdatedAt);
        Assert.False(await h.Auth.IsLoggedInAsync());
    }

    [Fact]
    public async Task Signing_out_revokes_the_refresh_token_on_the_server()
    {
        // Otherwise "signing out" only ever meant forgetting the token locally — a copy
        // captured earlier would still work, since nothing told the server the session ended.
        using var h = new SyncHarness();

        await h.Auth.LogoutAsync();

        Assert.Equal(["stub-refresh-token"], h.Api.RevokedTokens);
    }

    [Fact]
    public async Task Signing_out_succeeds_locally_even_when_the_server_is_unreachable()
    {
        using var h = new SyncHarness();
        h.Api.ThrowOnSend = true;

        await h.Auth.LogoutAsync();

        Assert.False(await h.Auth.IsLoggedInAsync());
    }

    [Fact]
    public async Task The_server_URL_is_a_fixed_constant_not_per_device_state()
    {
        // It used to be a preference the user could set, and a stray value left behind on a
        // device had no UI to fix it. Signing out — or anything else — must not change it.
        using var h = new SyncHarness();

        await h.Auth.LogoutAsync();

        IAuthService authInterface = h.Auth;
        Assert.Equal(ExpenseTracker.Infrastructure.External.AuthService.ApiBaseUrl, authInterface.ApiBaseUrl);
    }
}
