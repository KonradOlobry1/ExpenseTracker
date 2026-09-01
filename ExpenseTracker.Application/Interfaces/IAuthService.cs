namespace ExpenseTracker.Application.Interfaces;

public record UserInfo(string Email, string UserId);

public interface IAuthService
{
    Task<bool> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<bool> RegisterAsync(string email, string password, CancellationToken ct = default);
    Task LogoutAsync();
    Task<bool> IsLoggedInAsync();
    Task<UserInfo?> GetCurrentUserAsync();
    Task<string?> GetTokenAsync();
    string? ApiBaseUrl { get; set; }
}
