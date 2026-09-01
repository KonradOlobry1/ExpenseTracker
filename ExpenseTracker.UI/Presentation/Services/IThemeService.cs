namespace ExpenseTracker.Presentation.Services;

public interface IThemeService
{
    bool IsDarkMode { get; }
    void Toggle();

    /// <summary>
    /// Applies a value rather than flipping one. Used when settings arrive from the account
    /// during sync, where the incoming value is absolute and toggling would invert it.
    /// </summary>
    void SetDarkMode(bool isDarkMode);

    event Action OnChanged;
}
