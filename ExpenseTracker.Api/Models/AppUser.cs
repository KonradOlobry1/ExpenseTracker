using Microsoft.AspNetCore.Identity;

namespace ExpenseTracker.Api.Models;

public class AppUser : IdentityUser
{
    // Display preferences live on the account rather than the device, so signing in on a new
    // phone or browser reproduces the same currency, language and theme. They ride along with
    // the ordinary sync payload; there is no separate settings endpoint.
    public string Currency { get; set; } = "USD";
    public string Language { get; set; } = "en";
    public bool IsDarkMode { get; set; }

    /// <summary>
    /// When the client that owns these values last changed them. Resolves the same way rows
    /// do — by the client's own clock, not arrival order — so the newer edit wins when two
    /// devices change settings while offline. Defaults to <see cref="DateTime.MinValue"/>,
    /// which lets the first device to sync seed the account.
    /// </summary>
    public DateTime SettingsUpdatedAt { get; set; }
}
