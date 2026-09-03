using ExpenseTracker.Application.Interfaces;

namespace ExpenseTracker.Presentation.Services;

public class ThemeService : IThemeService
{
    private const string PrefKey = "app_dark_mode";

    private readonly IPreferenceStore _prefs;
    private readonly LocalSettings _settings;

    public ThemeService(IPreferenceStore prefs, LocalSettings settings)
    {
        _prefs = prefs;
        _settings = settings;
        IsDarkMode = prefs.Get(PrefKey, false);
    }

    public bool IsDarkMode { get; private set; }

    public event Action? OnChanged;

    public void Toggle() => SetDarkMode(!IsDarkMode);

    public void SetDarkMode(bool isDarkMode)
    {
        if (isDarkMode == IsDarkMode) return;

        IsDarkMode = isDarkMode;
        _prefs.Set(PrefKey, isDarkMode);
        _settings.Touch();
        OnChanged?.Invoke();
    }
}
