using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ExpenseTracker.Contracts;

namespace ExpenseTracker.Api.Tests;

/// <summary>
/// Helpers for driving the API over HTTP the way a real client does.
/// </summary>
/// <remarks>
/// The payload types come from ExpenseTracker.Contracts rather than being re-declared here.
/// Hand-copied records made the suite agree with itself instead of with the server: a field
/// added to the contract simply would not appear in the test's copy, and every assertion
/// would keep passing.
/// </remarks>
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

    public static SyncCategoryDto Category(Guid id, string name = "Food", bool isDeleted = false)
        => new(id, name, "restaurant", "#F44336", true, new DateTime(2026, 1, 1), null, isDeleted);

    public static SyncExpenseDto Expense(Guid id, Guid categoryId, decimal amount = 10m,
        string description = "Test", DateTime? updatedAt = null, bool isDeleted = false)
        => new(id, description, amount, new DateTime(2026, 1, 15), categoryId, null,
               new DateTime(2026, 1, 15), updatedAt, isDeleted);
}
