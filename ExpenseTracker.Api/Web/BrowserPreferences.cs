using Microsoft.JSInterop;

namespace ExpenseTracker.Api.Web;

/// <summary>
/// Per-browser preference storage backed by cookies.
/// </summary>
/// <remarks>
/// The UI service interfaces are synchronous (<c>Selected</c>, <c>IsDarkMode</c>,
/// <c>CurrentLanguage</c> are plain properties), which rules out ProtectedLocalStorage —
/// that is async and only readable after the first render, so every page would render with
/// the default and then flip.
///
/// Cookies avoid that: they arrive on the request, so the correct value is available before
/// anything renders. They can only be *written* from a real HTTP response, though, and a
/// preference change happens inside a live circuit — hence writes go through JS
/// (<c>document.cookie</c>) while reads come from the request.
/// </remarks>
public class BrowserPreferences(IHttpContextAccessor accessor, IJSRuntime js)
{
    public string? Read(string key)
        => accessor.HttpContext?.Request.Cookies.TryGetValue(key, out var value) == true
            ? value
            : null;

    /// <summary>
    /// Persists a preference. Fire-and-forget: a failed write only means the preference does
    /// not outlive the session, which is not worth faulting a UI interaction over.
    /// </summary>
    public void Write(string key, string value) => _ = WriteAsync(key, value);

    private async Task WriteAsync(string key, string value)
    {
        try
        {
            await js.InvokeVoidAsync("appPrefs.set", key, value);
        }
        catch (JSDisconnectedException) { }        // circuit closed mid-write
        catch (InvalidOperationException) { }      // no JS yet (prerendering)
    }
}
