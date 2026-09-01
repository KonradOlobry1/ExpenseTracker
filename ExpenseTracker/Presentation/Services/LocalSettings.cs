namespace ExpenseTracker.Presentation.Services;

/// <summary>
/// Records when this device last changed a display preference.
/// </summary>
/// <remarks>
/// Currency, language and theme are stored on the account, and sync resolves them by the
/// client's own clock the same way rows are resolved. That needs a single stamp covering all
/// three, written by whichever service the user touched, and read by the sync service when it
/// builds its payload.
///
/// A device that has never changed a preference reports <see cref="DateTime.MinValue"/>, so
/// it can only ever lose — a fresh install adopts the account's settings instead of pushing
/// its defaults over them.
/// </remarks>
public static class LocalSettings
{
    public const string StampKey = "settings_updated_at";

    public static DateTime UpdatedAt
    {
        get
        {
            var ticks = Preferences.Default.Get<long>(StampKey, 0);
            return ticks == 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc);
        }
        set => Preferences.Default.Set(StampKey, value.Ticks);
    }

    /// <summary>Marks the preferences as changed on this device, now.</summary>
    public static void Touch() => UpdatedAt = DateTime.UtcNow;
}
