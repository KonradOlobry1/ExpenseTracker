using Android.Content;
using Android.Provider;
using ExpenseTracker.Presentation.Services;

namespace ExpenseTracker.Platforms.Android;

public class PaymentCaptureService : IPaymentCaptureService
{
    // Static event so the NotificationListenerService (which has no DI) can raise it
    private static event Action<decimal, string>? StaticPaymentDetected;

    private bool _disposed;

    public event Action<decimal, string>? PaymentDetected;

    public PaymentCaptureService()
    {
        StaticPaymentDetected += OnStaticPaymentDetected;
    }

    private void OnStaticPaymentDetected(decimal amount, string merchant)
        => PaymentDetected?.Invoke(amount, merchant);

    // Android can deliver the same payment notification more than once - Google Pay updates
    // its notifications in place, and each update reaches the listener as a new post - and
    // every delivery used to open its own prompt. The same amount at the same merchant inside
    // this window is one payment. Two real purchases that identical, that quickly, are rare
    // enough that suppressing the second costs less than a stack of duplicate dialogs.
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromSeconds(10);
    private static readonly object DuplicateGate = new();
    private static (decimal Amount, string Merchant, DateTime At)? _lastPayment;

    internal static void RaisePaymentDetected(decimal amount, string merchant)
    {
        lock (DuplicateGate)
        {
            if (_lastPayment is { } last
                && last.Amount == amount
                && last.Merchant == merchant
                && DateTime.UtcNow - last.At < DuplicateWindow)
                return;

            _lastPayment = (amount, merchant, DateTime.UtcNow);
        }

        StaticPaymentDetected?.Invoke(amount, merchant);
    }

    public bool IsAvailable => true;

    public bool IsPermissionGranted
    {
        get
        {
            var context = global::Android.App.Application.Context;
            var flat = Settings.Secure.GetString(
                context.ContentResolver,
                "enabled_notification_listeners");
            if (string.IsNullOrEmpty(flat)) return false;
            var packageName = context.PackageName;
            return flat.Contains(packageName ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }

    public void OpenPermissionSettings()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(Settings.ActionNotificationListenerSettings);
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);
    }

    // The static event roots this instance, so it can never be finalized — a
    // finalizer-based unsubscribe would never run. Detach deterministically instead.
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StaticPaymentDetected -= OnStaticPaymentDetected;
        PaymentDetected = null;
        GC.SuppressFinalize(this);
    }
}
