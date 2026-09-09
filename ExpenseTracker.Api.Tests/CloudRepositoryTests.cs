using ExpenseTracker.Api.Data;
using ExpenseTracker.Api.Data.Repositories;
using ExpenseTracker.Api.Models;
using ExpenseTracker.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// The repositories behind the web UI — the ones the Blazor pages read and write through.
/// </summary>
/// <remarks>
/// These had no tests at all, which was only discovered by breaking one on purpose: making a
/// delete read no-tracking, so its SaveChanges silently saved nothing, left the entire suite
/// green. Every API test drives the sync controller instead, and the sync controller does not
/// touch these classes.
///
/// The pairs below are deliberate. Each read is no-tracking, for speed, and each delete reads
/// through the same helper in order to mutate what it finds — so the read tests and the delete
/// tests together pin the one distinction that makes that safe.
/// </remarks>
public class CloudRepositoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class StubUser(string userId) : ICurrentUser
    {
        public string UserId { get; } = userId;
    }

    /// <summary>Creates a real account and returns repositories scoped to it.</summary>
    private async Task<(CloudCategoryRepository Categories, CloudExpenseRepository Expenses)> ForNewUserAsync()
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        var user = new AppUser
        {
            UserName = $"cloud-{Guid.NewGuid():N}@test.local",
            Email = $"cloud-{Guid.NewGuid():N}@test.local",
        };
        var created = await users.CreateAsync(user, "Passw0rd!");
        Assert.True(created.Succeeded, "the test account could not be created");

        var dbFactory = factory.Services.GetRequiredService<IDbContextFactory<ApiDbContext>>();
        var current = new StubUser(user.Id);

        return (new CloudCategoryRepository(dbFactory, current),
                new CloudExpenseRepository(dbFactory, current));
    }

    [Fact]
    public async Task A_created_category_comes_back_from_the_read()
    {
        var (categories, _) = await ForNewUserAsync();

        await categories.CreateAsync(new Category { Name = "Books", Icon = "book", Color = "#333" });
        var all = await categories.GetAllAsync();

        Assert.Contains(all, c => c.Name == "Books");
    }

    [Fact]
    public async Task Deleting_a_category_takes_it_out_of_the_read()
    {
        // The no-tracking regression test. DeleteAsync finds the row through the same helper
        // the reads use and then mutates it; if that read stopped tracking, SaveChanges would
        // write nothing and the category would still be here.
        var (categories, _) = await ForNewUserAsync();
        var created = await categories.CreateAsync(
            new Category { Name = "Temporary", Icon = "x", Color = "#111" });

        await categories.DeleteAsync(created.Id);
        var all = await categories.GetAllAsync();

        Assert.DoesNotContain(all, c => c.Id == created.Id);
    }

    [Fact]
    public async Task A_deleted_category_is_tombstoned_rather_than_removed()
    {
        // Soft delete, so the deletion can propagate to devices on their next pull. A hard
        // delete would simply be re-sent by the next device that pushes.
        var (categories, _) = await ForNewUserAsync();
        var created = await categories.CreateAsync(
            new Category { Name = "Doomed", Icon = "x", Color = "#111" });

        await categories.DeleteAsync(created.Id);

        await using var db = await factory.Services
            .GetRequiredService<IDbContextFactory<ApiDbContext>>().CreateDbContextAsync();
        var row = await db.Categories.AsNoTracking().SingleAsync(c => c.Id == created.Id);

        Assert.True(row.IsDeleted);
    }

    [Fact]
    public async Task A_system_category_refuses_to_be_deleted()
    {
        var (categories, _) = await ForNewUserAsync();
        var created = await categories.CreateAsync(
            new Category { Name = "Built in", Icon = "x", Color = "#111", IsSystem = true });

        await Assert.ThrowsAsync<InvalidOperationException>(() => categories.DeleteAsync(created.Id));
    }

    [Fact]
    public async Task An_edit_made_to_a_row_that_was_read_back_is_saved()
    {
        // Reads are no-tracking, so the entity handed to UpdateAsync is detached. That is fine
        // — Update re-attaches it to a fresh context — but it is the exact path a careless
        // change to the read would break, and nothing else here would notice.
        var (categories, _) = await ForNewUserAsync();
        await categories.CreateAsync(new Category { Name = "Before", Icon = "x", Color = "#111" });

        var read = (await categories.GetAllAsync()).Single(c => c.Name == "Before");
        read.Name = "After";
        await categories.UpdateAsync(read);

        var all = await categories.GetAllAsync();

        Assert.Contains(all, c => c.Name == "After");
        Assert.DoesNotContain(all, c => c.Name == "Before");
    }

    [Fact]
    public async Task Deleting_an_expense_takes_it_out_of_the_read()
    {
        var (categories, expenses) = await ForNewUserAsync();
        var category = await categories.CreateAsync(
            new Category { Name = "Food", Icon = "x", Color = "#111" });

        var created = await expenses.CreateAsync(new Expense
        {
            Description = "Lunch",
            Amount = 12.5m,
            Date = DateTime.Today,
            CategoryId = category.Id,
        });

        await expenses.DeleteAsync(created.Id);
        var all = await expenses.GetAllAsync();

        Assert.DoesNotContain(all, e => e.Id == created.Id);
    }

    [Fact]
    public async Task One_account_never_reads_another_accounts_rows()
    {
        // Every read filters on the current user. Worth pinning here as well as at the sync
        // boundary: these repositories are what the web UI renders from.
        var (aliceCategories, _) = await ForNewUserAsync();
        var (bobCategories, _) = await ForNewUserAsync();

        await aliceCategories.CreateAsync(
            new Category { Name = "Alice only", Icon = "x", Color = "#111" });

        var bobsCategories = await bobCategories.GetAllAsync();

        Assert.DoesNotContain(bobsCategories, c => c.Name == "Alice only");
    }
}
