namespace ExpenseTracker.Presentation.Services;

public interface IPaymentCaptureService : IDisposable
{
    event Action<decimal, string>? PaymentDetected;
    bool IsAvailable { get; }
    bool IsPermissionGranted { get; }
    void OpenPermissionSettings();
}
