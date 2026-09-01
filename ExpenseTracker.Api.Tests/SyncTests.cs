using System.Net;
using System.Net.Http.Json;
using static ExpenseTracker.Api.Tests.TestClient;

namespace ExpenseTracker.Api.Tests;

public class SyncTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Pushed_data_comes_back_on_pull()
    {
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        var push = await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 42.50m, "Round trip")],
            Categories: [Category(categoryId)]));
        Assert.Equal(HttpStatusCode.OK, push.StatusCode);

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        var expense = Assert.Single(pulled!.Expenses!);
        Assert.Equal(expenseId, expense.SyncId);
        Assert.Equal(42.50m, expense.Amount);
        Assert.Equal(categoryId, expense.CategorySyncId);
        Assert.Contains(pulled.Categories!, c => c.SyncId == categoryId);
    }

    [Fact]
    public async Task Pushing_the_same_SyncId_twice_updates_rather_than_duplicates()
    {
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 10m, "Original")],
            Categories: [Category(categoryId)]));

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 99m, "Edited")]));

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        var expense = Assert.Single(pulled!.Expenses!);
        Assert.Equal(99m, expense.Amount);
        Assert.Equal("Edited", expense.Description);
    }

    [Fact]
    public async Task One_account_never_sees_another_accounts_rows()
    {
        var alice = await factory.RegisterAsync();
        var bob = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();

        await alice.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(Guid.NewGuid(), categoryId, 500m, "Alice's rent")],
            Categories: [Category(categoryId, "Housing")]));

        var bobsPull = await (await bob.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        Assert.Empty(bobsPull!.Expenses ?? []);
        // Bob has his own seeded built-ins, but none of Alice's rows.
        Assert.DoesNotContain(bobsPull.Categories ?? [], c => c.SyncId == categoryId);
    }

    [Fact]
    public async Task Two_accounts_can_hold_the_same_SyncId_independently()
    {
        // SyncId is unique per user, not globally — the seeded categories deliberately
        // share fixed SyncIds across every install.
        var alice = await factory.RegisterAsync();
        var bob = await factory.RegisterAsync();
        var shared = new Guid("11111111-0000-0000-0000-000000000001");

        await alice.PostAsJsonAsync("/api/sync/push",
            new PushPayload(Categories: [Category(shared, "Food")]));
        var bobPush = await bob.PostAsJsonAsync("/api/sync/push",
            new PushPayload(Categories: [Category(shared, "Food")]));

        Assert.Equal(HttpStatusCode.OK, bobPush.StatusCode);
        var bobsPull = await (await bob.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();
        Assert.Contains(bobsPull!.Categories!, c => c.SyncId == shared);
    }

    [Fact]
    public async Task Pull_since_a_timestamp_returns_only_newer_rows()
    {
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(Guid.NewGuid(), categoryId, 10m, "Before")],
            Categories: [Category(categoryId)]));

        var cutoff = DateTime.UtcNow;
        await Task.Delay(50);

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(Guid.NewGuid(), categoryId, 20m, "After")]));

        var pulled = await (await client.GetAsync($"/api/sync/pull?since={cutoff:O}")).ReadAsync<PullResponse>();

        Assert.Equal("After", Assert.Single(pulled!.Expenses!).Description);
    }

    [Fact]
    public async Task Pull_without_since_returns_everything()
    {
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses:
            [
                Expense(Guid.NewGuid(), categoryId, 1m, "One"),
                Expense(Guid.NewGuid(), categoryId, 2m, "Two"),
            ],
            Categories: [Category(categoryId)]));

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        Assert.Equal(2, pulled!.Expenses!.Count);
    }

    [Fact]
    public async Task An_empty_push_is_accepted()
    {
        var client = await factory.RegisterAsync();
        var response = await client.PostAsJsonAsync("/api/sync/push", new PushPayload());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

/// <summary>
/// Regression cover for the defect that shipped this release: the seeded categories all
/// carried Guid.Empty as their SyncId, so every expense pointed at the same category and a
/// second device would have collapsed the whole history onto one.
/// </summary>
public class EmptySyncIdGuardTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_category_with_an_empty_SyncId_is_rejected()
    {
        var client = await factory.RegisterAsync();

        var response = await client.PostAsJsonAsync("/api/sync/push",
            new PushPayload(Categories: [Category(Guid.Empty)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_expense_with_an_empty_SyncId_is_rejected()
    {
        var client = await factory.RegisterAsync();

        var response = await client.PostAsJsonAsync("/api/sync/push",
            new PushPayload(Expenses: [Expense(Guid.Empty, Guid.NewGuid())]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_expense_pointing_at_an_empty_category_is_rejected()
    {
        var client = await factory.RegisterAsync();

        var response = await client.PostAsJsonAsync("/api/sync/push",
            new PushPayload(Expenses: [Expense(Guid.NewGuid(), Guid.Empty)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_rejected_push_writes_nothing()
    {
        var client = await factory.RegisterAsync();
        var goodCategory = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(Guid.NewGuid(), goodCategory, 10m, "Valid")],
            Categories: [Category(goodCategory)]));

        // One good row, one empty-SyncId row: the whole batch must be refused.
        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses:
            [
                Expense(Guid.NewGuid(), goodCategory, 20m, "Would-be valid"),
                Expense(Guid.Empty, goodCategory, 30m, "Invalid"),
            ]));

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        Assert.Equal("Valid", Assert.Single(pulled!.Expenses!).Description);
    }
}
