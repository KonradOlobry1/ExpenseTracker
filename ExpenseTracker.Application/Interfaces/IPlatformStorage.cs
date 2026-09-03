namespace ExpenseTracker.Application.Interfaces;

/// <summary>
/// Small key/value storage for user preferences, backed by whatever the host platform offers.
/// </summary>
/// <remarks>
/// This exists so the sync and auth services can be tested. They used to call MAUI's static
/// <c>Preferences.Default</c> directly, which pinned roughly five hundred lines of conflict
/// resolution, tombstone handling and settings merging inside the app head where no test could
/// reach them — the most defect-prone code in the project, and the only part with no direct
/// cover.
///
/// Not for secrets: see <see cref="ISecureStore"/>.
/// </remarks>
public interface IPreferenceStore
{
    /// <summary>The stored value, or <paramref name="defaultValue"/> if the key is absent.</summary>
    T Get<T>(string key, T defaultValue);

    void Set<T>(string key, T value);

    void Remove(string key);
}

/// <summary>
/// Storage for the one secret this app holds on a device: the bearer token.
/// </summary>
/// <remarks>
/// Separate from <see cref="IPreferenceStore"/> because the platform implementations are
/// genuinely different — this one is backed by the OS keystore and is asynchronous because
/// reading from it can be.
/// </remarks>
public interface ISecureStore
{
    Task<string?> GetAsync(string key);

    Task SetAsync(string key, string value);

    void Remove(string key);
}
