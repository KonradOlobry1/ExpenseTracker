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

    public void Toggle()
    {
        IsDarkMode = !IsDarkMode;
        Preferences.Default.Set(PrefKey, IsDarkMode);
        OnChanged?.Invoke();
    }
}
