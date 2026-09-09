namespace ExpenseTracker.Api.DTOs;
public record LoginRequest(string Email, string Password);
public record RegisterRequest(string Email, string Password);
public record AuthResponse(string Token, DateTime Expiry, string RefreshToken);
public record RefreshRequest(string RefreshToken);

public record ForgotPasswordRequest(string Email);

/// <summary>
/// No email field on purpose. The token is 256 bits and globally unique, so it already
/// identifies the account; asking for the address as well would only add a second way to probe
/// which addresses are registered.
/// </summary>
public record ResetPasswordRequest(string Token, string NewPassword);
