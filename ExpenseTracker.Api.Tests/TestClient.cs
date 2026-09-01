using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ExpenseTracker.Api.Tests;

internal static class TestClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Registers a fresh account and returns a client that authenticates as it.</summary>
    public static async Task<HttpClient> RegisterAsync(this ApiFactory factory, string? email = null)
    {
        var client = factory.CreateClient();
        email ??= $"user-{Guid.NewGuid():N}@test.local";

        var response = await client.PostAsJsonAsync("/api/auth/register",
            new { Email = email, Password = "Passw0rd!" });
        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        return client;
    }

    public static async Task<T?> ReadAsync<T>(this HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<T>(Json);

    public record AuthResponse(string Token, DateTime Expiry);

    public record PushExpense(Guid SyncId, string Description, decimal Amount, DateTime Date,
        Guid CategorySyncId, string? Notes, DateTime CreatedAt, DateTime? UpdatedAt,
        bool IsDeleted = false);


    public record PushCategory(Guid SyncId, string Name, string Icon, string Color,
        bool IsSystem, DateTime CreatedAt, DateTime? UpdatedAt, bool IsDeleted = false);

    public record PushPayload(
        List<PushExpense>? Expenses = null,
        List<object>? Incomes = null,
        List<object>? Subscriptions = null,
        List<PushCategory>? Categories = null);

    public record PullResponse(
        List<PushExpense>? Expenses,
        List<object>? Incomes,
        List<object>? Subscriptions,
        List<PushCategory>? Categories,
        DateTime ServerTime);

    public static PushCategory Category(Guid id, string name = "Food", bool isDeleted = false)
        => new(id, name, "restaurant", "#F44336", true, new DateTime(2026, 1, 1), null, isDeleted);

    public static PushExpense Expense(Guid id, Guid categoryId, decimal amount = 10m,
        string description = "Test", DateTime? updatedAt = null, bool isDeleted = false)
        => new(id, description, amount, new DateTime(2026, 1, 15), categoryId, null,
               new DateTime(2026, 1, 15), updatedAt, isDeleted);
}
