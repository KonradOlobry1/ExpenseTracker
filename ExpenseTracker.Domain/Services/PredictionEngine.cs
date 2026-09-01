using ExpenseTracker.Domain.Entities;

namespace ExpenseTracker.Domain.Services;

public static class PredictionEngine
{
    /// <summary>Returns the next billing date on or after <paramref name="referenceDate"/>.</summary>
    public static DateTime? NextOccurrence(DateTime startDate, BillingCycle cycle, DateTime referenceDate)
    {
        if (startDate > referenceDate)
            return startDate;

        var current = startDate;
        while (current <= referenceDate)
            current = Advance(current, cycle);

        return current;
    }

    /// <summary>Returns the next <paramref name="count"/> billing dates starting from <paramref name="fromDate"/>.</summary>
    public static List<DateTime> ForecastOccurrences(DateTime startDate, BillingCycle cycle, DateTime fromDate, int count)
    {
        var results = new List<DateTime>(count);
        var current = NextOccurrence(startDate, cycle, fromDate) ?? startDate;

        for (int i = 0; i < count; i++)
        {
            results.Add(current);
            current = Advance(current, cycle);
        }
        return results;
    }

    /// <summary>Returns all occurrences within [<paramref name="windowStart"/>, <paramref name="windowEnd"/>].</summary>
    public static List<DateTime> OccurrencesInRange(
        DateTime startDate, BillingCycle cycle, DateTime windowStart, DateTime windowEnd)
    {
        var results = new List<DateTime>();
        var current = NextOccurrence(startDate, cycle, windowStart) ?? startDate;

        while (current <= windowEnd)
        {
            results.Add(current);
            current = Advance(current, cycle);
        }
        return results;
    }

    /// <summary>Converts any billing cycle amount to its monthly equivalent.</summary>
    public static decimal ToMonthlyEquivalent(decimal amount, BillingCycle cycle) =>
        cycle switch
        {
            BillingCycle.Weekly    => amount * 52m / 12m,
            BillingCycle.Monthly   => amount,
            BillingCycle.Quarterly => amount / 3m,
            BillingCycle.Yearly    => amount / 12m,
            _                      => amount
        };

    private static DateTime Advance(DateTime date, BillingCycle cycle) =>
        cycle switch
        {
            BillingCycle.Weekly    => date.AddDays(7),
            BillingCycle.Monthly   => AddMonthSafe(date, 1),
            BillingCycle.Quarterly => AddMonthSafe(date, 3),
            BillingCycle.Yearly    => AddMonthSafe(date, 12),
            _                      => date.AddMonths(1)
        };

    private static DateTime AddMonthSafe(DateTime date, int months)
    {
        var target = date.AddMonths(months);
        var daysInTarget = DateTime.DaysInMonth(target.Year, target.Month);
        return new DateTime(target.Year, target.Month, Math.Min(date.Day, daysInTarget),
            date.Hour, date.Minute, date.Second, date.Kind);
    }
}
