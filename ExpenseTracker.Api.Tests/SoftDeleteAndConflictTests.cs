using System.Net;
using System.Net.Http.Json;
using static ExpenseTracker.Api.Tests.TestClient;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// Deletions used to vanish silently: nothing carried a tombstone, so a row deleted on one
/// device was merely absent from its next push, the server kept it, and the next pull put it
/// back. These pin the fix.
/// </summary>
public class SoftDeleteTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_tombstone_is_stored_and_returned_on_pull()
    {
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 10m, "Doomed")],
            Categories: [Category(categoryId)]));

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 10m, "Doomed",
                               updatedAt: new DateTime(2026, 2, 1), isDeleted: true)]));

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        var expense = Assert.Single(pulled!.Expenses!);
        Assert.True(expense.IsDeleted);
    }

    [Fact]
    public async Task A_deleted_row_is_not_resurrected_by_a_later_push_of_the_same_id()
    {
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 10m, "First")],
            Categories: [Category(categoryId)]));

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 10m, "First",
                               updatedAt: new DateTime(2026, 2, 1), isDeleted: true)]));

        // A second device that still has the row pushes its older copy back up.
        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 10m, "First",
                               updatedAt: new DateTime(2026, 1, 20), isDeleted: false)]));

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        Assert.True(Assert.Single(pulled!.Expenses!).IsDeleted);
    }

    [Fact]
    public async Task Deleting_a_category_tombstones_it_rather_than_dropping_it()
    {
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/sync/push",
            new PushPayload(Categories: [Category(categoryId, "Temporary")]));
        await client.PostAsJsonAsync("/api/sync/push",
            new PushPayload(Categories: [Category(categoryId, "Temporary", isDeleted: true)]));

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        Assert.True(Assert.Single(pulled!.Categories!).IsDeleted);
    }
}

/// <summary>
/// Push used to apply whatever arrived last, so the device that synced second won even when
/// its edit was older. Resolution is now by the client's own edit timestamp.
/// </summary>
public class ConflictResolutionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task A_newer_edit_overwrites_an_older_one()
    {
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 10m, "Old", updatedAt: new DateTime(2026, 1, 1))],
            Categories: [Category(categoryId)]));

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 99m, "New", updatedAt: new DateTime(2026, 3, 1))]));

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        Assert.Equal("New", Assert.Single(pulled!.Expenses!).Description);
    }

    [Fact]
    public async Task A_stale_edit_arriving_late_does_not_clobber_a_newer_one()
    {
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 99m, "Newer", updatedAt: new DateTime(2026, 3, 1))],
            Categories: [Category(categoryId)]));

        // Device B was offline while editing; its change is older but reaches the server later.
        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 10m, "Older", updatedAt: new DateTime(2026, 1, 1))]));

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        Assert.Equal("Newer", Assert.Single(pulled!.Expenses!).Description);
        Assert.Equal(99m, Assert.Single(pulled.Expenses!).Amount);
    }

    [Fact]
    public async Task An_edit_with_the_same_timestamp_is_applied()
    {
        // Equal stamps are not stale — a same-second edit should still land rather than be
        // silently dropped.
        var client = await factory.RegisterAsync();
        var categoryId = Guid.NewGuid();
        var expenseId = Guid.NewGuid();
        var stamp = new DateTime(2026, 2, 2);

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 10m, "First", updatedAt: stamp)],
            Categories: [Category(categoryId)]));

        await client.PostAsJsonAsync("/api/sync/push", new PushPayload(
            Expenses: [Expense(expenseId, categoryId, 20m, "Second", updatedAt: stamp)]));

        var pulled = await (await client.GetAsync("/api/sync/pull")).ReadAsync<PullResponse>();

        Assert.Equal("Second", Assert.Single(pulled!.Expenses!).Description);
    }
}
