using ExpenseTracker.Domain.ValueObjects;

namespace ExpenseTracker.Presentation.Services;

public interface ICurrencyService
{
    CurrencyInfo Selected { get; }
    IReadOnlyList<CurrencyInfo> Available { get; }
    void SetCurrency(string code);
    string Format(decimal amount, string format = "C2");
    event Action? OnChanged;
}
