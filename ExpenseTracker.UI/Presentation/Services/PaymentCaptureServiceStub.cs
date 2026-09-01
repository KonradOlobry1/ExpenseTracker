namespace ExpenseTracker.Presentation.Services;

/// <summary>
/// No-op implementation for platforms without notification-listener support
/// (iOS, Windows, macOS). Never raises <see cref="PaymentDetected"/>.
/// </summary>
public class PaymentCaptureServiceStub : IPaymentCaptureService
{
    // Explicit accessors: the interface requires the event, but nothing on this
    // platform can raise it. A field-like event would warn CS0067.
    public event Action<decimal, string>? PaymentDetected
    {
        add { }
        remove { }
    }

    public bool IsAvailable => false;
    public bool IsPermissionGranted => false;
    public void OpenPermissionSettings() { }
    public void Dispose() { }
}
