using Android.App;
using Android.Content;
using Android.Content.PM;
using ExpenseTracker.Contracts;
using Microsoft.Maui.Authentication;

namespace ExpenseTracker.Platforms.Android;

/// <summary>
/// Receives <c>expensetracker://auth</c> when the system browser finishes the Microsoft
/// sign-in and hands it to the waiting <c>WebAuthenticator</c> call.
/// </summary>
/// <remarks>
/// The scheme and host must match <see cref="ExternalSignIn.CallbackUrl"/>, which the server
/// redirects to. Attribute arguments have to be constants, so the literals are repeated here.
/// </remarks>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "expensetracker",
    DataHost = "auth")]
public class WebAuthenticationCallbackActivity : WebAuthenticatorCallbackActivity
{
}
