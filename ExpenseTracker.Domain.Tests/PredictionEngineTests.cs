using ExpenseTracker.Domain.Entities;
using ExpenseTracker.Domain.Services;

namespace ExpenseTracker.Domain.Tests;

public class NextOccurrenceTests
{
    [Fact]
    public void Returns_the_start_date_when_the_subscription_has_not_begun()
    {
        var start = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Local);
        var next = PredictionEngine.NextOccurrence(start, BillingCycle.Monthly, new DateTime(2026, 1, 1));
        Assert.Equal(start, next);
    }

    [Theory]
    [InlineData(BillingCycle.Weekly, "2026-01-08")]
    [InlineData(BillingCycle.Monthly, "2026-02-01")]
    [InlineData(BillingCycle.Quarterly, "2026-04-01")]
    [InlineData(BillingCycle.Yearly, "2027-01-01")]
    public void Advances_by_one_cycle_from_the_start_date(BillingCycle cycle, string expected)
    {
        var next = PredictionEngine.NextOccurrence(
            new DateTime(2026, 1, 1), cycle, new DateTime(2026, 1, 1));

        Assert.Equal(DateTime.Parse(expected), next);
    }

    [Fact]
    public void Reference_date_landing_exactly_on_a_billing_date_returns_the_following_one()
    {
        // The loop is `while (current <= referenceDate)`, so a due date that is *today*
        // is treated as already billed.
        var next = PredictionEngine.NextOccurrence(
            new DateTime(2026, 1, 15), BillingCycle.Monthly, new DateTime(2026, 2, 15));

        Assert.Equal(new DateTime(2026, 3, 15), next);
    }

    [Fact]
    public void Clamps_to_the_last_day_when_the_target_month_is_shorter()
    {
        var next = PredictionEngine.NextOccurrence(
            new DateTime(2026, 1, 31), BillingCycle.Monthly, new DateTime(2026, 1, 31));

        Assert.Equal(new DateTime(2026, 2, 28), next);
    }

    [Fact]
    public void Clamping_is_permanent_once_it_happens()
    {
        // Documents current behaviour rather than endorsing it: Advance() operates on the
        // already-clamped date, so a 31st subscription silently becomes a 28th forever
        // instead of recovering to 31 March. See the note in the accompanying report.
        var dates = PredictionEngine.ForecastOccurrences(
            new DateTime(2026, 1, 31), BillingCycle.Monthly, new DateTime(2026, 1, 31), 3);

        Assert.Equal(new DateTime(2026, 2, 28), dates[0]);
        Assert.Equal(new DateTime(2026, 3, 28), dates[1]);   // not 31 March
        Assert.Equal(new DateTime(2026, 4, 28), dates[2]);
    }

    [Fact]
    public void Handles_a_leap_year_february()
    {
        var next = PredictionEngine.NextOccurrence(
            new DateTime(2028, 1, 31), BillingCycle.Monthly, new DateTime(2028, 1, 31));

        Assert.Equal(new DateTime(2028, 2, 29), next);
    }
}

public class ForecastOccurrencesTests
{
    [Fact]
    public void Returns_exactly_the_requested_number_of_dates_in_ascending_order()
    {
        var dates = PredictionEngine.ForecastOccurrences(
            new DateTime(2026, 1, 1), BillingCycle.Monthly, new DateTime(2026, 1, 1), 5);

        Assert.Equal(5, dates.Count);
        Assert.Equal(dates.OrderBy(d => d), dates);
    }

    [Fact]
    public void Returns_an_empty_list_for_a_zero_count()
    {
        var dates = PredictionEngine.ForecastOccurrences(
            new DateTime(2026, 1, 1), BillingCycle.Monthly, new DateTime(2026, 1, 1), 0);

        Assert.Empty(dates);
    }
}

public class OccurrencesInRangeTests
{
    [Fact]
    public void Returns_every_occurrence_inside_the_window()
    {
        var dates = PredictionEngine.OccurrencesInRange(
            new DateTime(2026, 1, 1), BillingCycle.Monthly,
            new DateTime(2026, 1, 1), new DateTime(2026, 6, 30));

        Assert.Equal(5, dates.Count);   // Feb–Jun; January is excluded, see NextOccurrence
        Assert.All(dates, d => Assert.InRange(d, new DateTime(2026, 1, 1), new DateTime(2026, 6, 30)));
    }

    [Fact]
    public void Returns_nothing_when_the_window_closes_before_the_next_billing_date()
    {
        var dates = PredictionEngine.OccurrencesInRange(
            new DateTime(2026, 1, 1), BillingCycle.Yearly,
            new DateTime(2026, 2, 1), new DateTime(2026, 3, 1));

        Assert.Empty(dates);
    }

    [Fact]
    public void Weekly_billing_yields_one_occurrence_per_week()
    {
        var dates = PredictionEngine.OccurrencesInRange(
            new DateTime(2026, 1, 1), BillingCycle.Weekly,
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 29));

        Assert.Equal(4, dates.Count);
    }
}

public class ToMonthlyEquivalentTests
{
    [Theory]
    [InlineData(BillingCycle.Monthly, 12, 12)]
    [InlineData(BillingCycle.Quarterly, 30, 10)]
    [InlineData(BillingCycle.Yearly, 120, 10)]
    public void Normalises_each_cycle_to_a_monthly_cost(BillingCycle cycle, decimal amount, decimal expected)
        => Assert.Equal(expected, PredictionEngine.ToMonthlyEquivalent(amount, cycle));

    [Fact]
    public void Weekly_uses_52_weeks_per_year_not_4_weeks_per_month()
    {
        // 10/week is 43.33/month (52/12), not 40 — the naive x4 would understate by ~8%.
        var monthly = PredictionEngine.ToMonthlyEquivalent(10m, BillingCycle.Weekly);
        Assert.Equal(43.33m, Math.Round(monthly, 2));
    }

    [Fact]
    public void Keeps_full_decimal_precision()
    {
        // Guards against anyone "simplifying" this to double, which would reintroduce
        // floating-point drift into money.
        var monthly = PredictionEngine.ToMonthlyEquivalent(100m, BillingCycle.Quarterly);
        Assert.Equal(100m / 3m, monthly);
    }
}
