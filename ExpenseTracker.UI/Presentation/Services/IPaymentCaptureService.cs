namespace ExpenseTracker.Presentation.Services;

/// <summary>
/// Watches for payment notifications from banking apps and offers to turn them into expenses.
/// </summary>
/// <remarks>
/// Android only. Every other head gets a stub whose <see cref="IsAvailable"/> is false, and the
/// UI asks that rather than checking which platform it is on — the same capability-flag shape
/// as <c>ISyncService.IsSupported</c>.
/// </remarks>
public interface IPaymentCaptureService : IDisposable
{
    /// <summary>Raised with the amount and the merchant when a payment notification is read.</summary>
    event Action<decimal, string>? PaymentDetected;

    /// <summary>
    /// Whether this platform can capture payments at all. False everywhere but Android, and
    /// false regardless of whether permission has been granted.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Whether the user has granted notification access. Distinct from
    /// <see cref="IsAvailable"/>: capable but not permitted is the case worth prompting about.
    /// </summary>
    bool IsPermissionGranted { get; }

    /// <summary>
    /// Opens the system settings page for notification access. Android cannot request this
    /// permission through a dialog — the user has to grant it in Settings.
    /// </summary>
    void OpenPermissionSettings();
}
