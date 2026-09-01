namespace ExpenseTracker.Presentation.Services;

public class ThemeService : IThemeService
{
    private const string PrefKey = "app_dark_mode";

    public bool IsDarkMode { get; private set; }

    public event Action? OnChanged;

    public ThemeService()
    {
        IsDarkMode = Preferences.Default.Get(PrefKey, false);
    }

    public void Toggle() => SetDarkMode(!IsDarkMode);

    public void SetDarkMode(bool isDarkMode)
    {
        if (isDarkMode == IsDarkMode) return;

        IsDarkMode = isDarkMode;
        Preferences.Default.Set(PrefKey, isDarkMode);
        LocalSettings.Touch();
        OnChanged?.Invoke();
    }
}
