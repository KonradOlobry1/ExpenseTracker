namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// Opens a sign-in page in the system browser and waits for it to redirect back to the app.
/// </summary>
/// <remarks>
/// The one MAUI API the Microsoft sign-in needs (<c>WebAuthenticator</c>), kept behind an
/// interface for the same reason as <see cref="IPreferenceStore"/>: everything around it is
/// ordinary code that can be tested without a device.
///
/// Where a platform cannot do this, <see cref="IsAvailable"/> is false and the UI does not
/// offer the button - an inert control looks identical to a broken one.
/// </remarks>
public interface IExternalAuthBrowser
{
    bool IsAvailable { get; }

    /// <summary>
    /// Shows <paramref name="startUrl"/> and returns the query parameters of the address the
    /// browser was finally redirected to, or null if the user backed out.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>?> AuthenticateAsync(
        Uri startUrl, Uri callbackUrl, CancellationToken ct = default);
}
