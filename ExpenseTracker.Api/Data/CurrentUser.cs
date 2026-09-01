using System.Security.Claims;

namespace ExpenseTracker.Api.Data;

/// <summary>The signed-in account, as seen by the cloud repositories.</summary>
public interface ICurrentUser
{
    string UserId { get; }
}

public class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string UserId =>
        accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No signed-in user.");
}
