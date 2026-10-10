using ExpenseTracker.Api.Models;

namespace ExpenseTracker.Api.Web;

/// <summary>
/// Copies an account's display preferences into the browser's cookies.
/// </summary>
/// <remarks>
/// The UI services read these cookies from the incoming request, which is the only way a
/// synchronous property can have the right value before the first render. Sign-in is the
/// one place in the web app that is a real HTTP response rather than a circuit, so it is
/// the one place that can set them. Without it, a browser that has never seen this account
/// starts on the defaults and the user's currency and language appear to have been forgotten.
///
/// Shared by every sign-in route - the password form and Microsoft sign-in - so none of them
/// can forget it.
/// </remarks>
internal static class SettingsCookies
{
    public static void Write(HttpResponse response, AppUser user)
    {
        var options = new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true,   // a display preference the user set, not tracking
            SameSite = SameSiteMode.Lax,
            Path = "/"
        };

        response.Cookies.Append(PrefKeys.Currency, user.Currency, options);
        response.Cookies.Append(PrefKeys.Language, user.Language, options);
        response.Cookies.Append(PrefKeys.Theme, user.IsDarkMode ? "dark" : "light", options);
    }
}
