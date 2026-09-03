using ExpenseTracker.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace ExpenseTracker.Infrastructure.Platform;

/// <summary>
/// The MAUI implementations of the two platform stores. Everything else in the app talks to
/// the interfaces, which is what makes the sync and auth services testable off-device.
/// </summary>
public class MauiPreferenceStore : IPreferenceStore
{
    public T Get<T>(string key, T defaultValue) => Preferences.Default.Get(key, defaultValue);

    public void Set<T>(string key, T value) => Preferences.Default.Set(key, value);

    public void Remove(string key) => Preferences.Default.Remove(key);
}

/// <summary>
/// Backed by the OS keystore.
/// </summary>
/// <remarks>
/// Reads and writes are wrapped: the keystore is genuinely unavailable on some devices — a
/// broken Android keystore, or a Windows unpackaged build with no credential locker — and
/// throws rather than returning nothing. Callers treat a missing token as signed out, which
/// is the right outcome either way, so a failure here must not surface as a crash.
/// </remarks>
public class MauiSecureStore(ILogger<MauiSecureStore> logger) : ISecureStore
{
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            return await SecureStorage.Default.GetAsync(key);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Secure storage could not be read.");
            return null;
        }
    }

    public async Task SetAsync(string key, string value)
    {
        try
        {
            await SecureStorage.Default.SetAsync(key, value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Secure storage could not be written.");
        }
    }

    public void Remove(string key) => SecureStorage.Default.Remove(key);
}
