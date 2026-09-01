using Android.App;
using Android.Content;
using Android.Service.Notification;
using Android.OS;
using System.Text.RegularExpressions;

namespace ExpenseTracker.Platforms.Android;

[Service(
    Label = "ExpenseTracker Notification Listener",
    Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(new[] { "android.service.notification.NotificationListenerService" })]
public class PaymentNotificationService : NotificationListenerService
{
    // Google Pay package names
    private static readonly string[] GooglePayPackages =
    {
        "com.google.android.apps.walletnfcrel",
        "com.google.android.apps.nbu.paisa.user",
        "com.android.vending"
    };

    // Patterns to detect payment amounts like "PLN 25.99", "$12.50", "25,99 zł", "€9.99"
    private static readonly Regex AmountRegex = new(
        @"(?:PLN|USD|EUR|GBP|CHF|CZK|HUF|SEK|NOK|DKK)?\s*(\d{1,6}[.,]\d{2})\s*(?:PLN|USD|EUR|GBP|zł|€|\$|£)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override void OnNotificationPosted(StatusBarNotification? sbn)
    {
        if (sbn is null) return;
        if (!IsGooglePayPackage(sbn.PackageName)) return;

        var extras = sbn.Notification?.Extras;
        if (extras is null) return;

        var title = extras.GetString(global::Android.App.Notification.ExtraTitle) ?? "";
        var text  = extras.GetString(global::Android.App.Notification.ExtraText) ?? "";
        var combined = $"{title} {text}";

        var amount = ExtractAmount(combined);
        if (amount <= 0) return;

        var merchant = ExtractMerchant(title, text);

        PaymentCaptureService.RaisePaymentDetected(amount, merchant);
    }

    private static bool IsGooglePayPackage(string? packageName)
        => packageName is not null
           && GooglePayPackages.Any(p => packageName.StartsWith(p, StringComparison.OrdinalIgnoreCase));

    private static decimal ExtractAmount(string text)
    {
        var match = AmountRegex.Match(text);
        if (!match.Success) return 0;
        var raw = match.Groups[1].Value.Replace(',', '.');
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var val) ? val : 0;
    }

    private static string ExtractMerchant(string title, string text)
    {
        // Try to find merchant name — often in the title after "Payment to" or similar
        var merchantPatterns = new[]
        {
            new Regex(@"(?:paid to|payment to|at|purchase at|zapłacono w|płatność w)\s+(.+?)(?:\s+for|\s+\d|$)", RegexOptions.IgnoreCase),
        };

        foreach (var pattern in merchantPatterns)
        {
            var m = pattern.Match($"{title} {text}");
            if (m.Success) return m.Groups[1].Value.Trim();
        }

        // Fallback: use the title without the amount
        return AmountRegex.Replace(title, "").Trim().TrimEnd('-', ':', ' ');
    }
}
