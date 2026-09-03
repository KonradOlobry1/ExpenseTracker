using ExpenseTracker.Application.Interfaces;

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
public class LocalSettings(IPreferenceStore prefs)
{
    public const string StampKey = "settings_updated_at";

    public DateTime UpdatedAt
    {
        get
        {
            var ticks = prefs.Get<long>(StampKey, 0);
            return ticks == 0 ? DateTime.MinValue : new DateTime(ticks, DateTimeKind.Utc);
        }
        set => prefs.Set(StampKey, value.Ticks);
    }

    /// <summary>Marks the preferences as changed on this device, now.</summary>
    public void Touch() => UpdatedAt = DateTime.UtcNow;

    /// <summary>Forgets the stamp, so the next sync adopts whatever the account holds.</summary>
    public void Clear() => prefs.Remove(StampKey);
}
