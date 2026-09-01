namespace ExpenseTracker.Presentation.Services;

public interface IThemeService
{
    bool IsDarkMode { get; }
    void Toggle();
    event Action OnChanged;
}
