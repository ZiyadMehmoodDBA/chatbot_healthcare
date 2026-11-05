using AiChatbot.Reporting;
using Xunit;

namespace AiChatbot.Tests;

public class PeriodTests
{
    // Thursday 2026-06-04
    private static readonly DateOnly Today = new(2026, 6, 4);

    private static DateRange One(string text)
    {
        var found = Periods.Find(text, Today);
        Assert.Single(found);
        return found[0].Range;
    }

    [Theory]
    [InlineData("payments this month", "2026-06-01", "2026-06-04")]
    [InlineData("payments MTD", "2026-06-01", "2026-06-04")]
    [InlineData("payments last month", "2026-05-01", "2026-05-31")]
    [InlineData("denials this quarter", "2026-04-01", "2026-06-04")]
    [InlineData("denials last quarter", "2026-01-01", "2026-03-31")]
    [InlineData("claims ytd", "2026-01-01", "2026-06-04")]
    [InlineData("claims last year", "2025-01-01", "2025-12-31")]
    [InlineData("claims in 2025", "2025-01-01", "2025-12-31")]
    [InlineData("payments last 30 days", "2026-05-06", "2026-06-04")]
    [InlineData("payments last 2 weeks", "2026-05-22", "2026-06-04")]
    [InlineData("payments last 3 months", "2026-03-05", "2026-06-04")]
    [InlineData("payments this week", "2026-06-01", "2026-06-04")]
    [InlineData("payments last week", "2026-05-25", "2026-05-31")]
    [InlineData("payments yesterday", "2026-06-03", "2026-06-03")]
    [InlineData("payments Q1 2026", "2026-01-01", "2026-03-31")]
    [InlineData("payments in April", "2026-04-01", "2026-04-30")]
    [InlineData("payments April 2025", "2025-04-01", "2025-04-30")]
    public void Named_periods_resolve_to_exact_dates(string text, string start, string end)
    {
        var r = One(text);
        Assert.Equal(DateOnly.Parse(start), r.Start);
        Assert.Equal(DateOnly.Parse(end), r.End);
    }

    [Fact]
    public void A_month_that_has_not_happened_this_year_means_last_year()
        => Assert.Equal(new DateOnly(2025, 12, 1), One("payments in December").Start);

    [Fact]
    public void Q4_said_in_June_means_last_years_Q4()
        => Assert.Equal(new DateOnly(2025, 10, 1), One("denials Q4").Start);

    [Fact]
    public void The_verb_may_is_not_a_month()
        => Assert.Empty(Periods.Find("may I see payments", Today));

    [Fact]
    public void The_month_may_is_found_after_a_preposition()
        => Assert.Equal(new DateOnly(2026, 5, 1), One("payments for may").Start);

    [Theory]
    [InlineData("2026-05-01", "2026-05-31", "2026-04-01", "2026-04-30")]   // month -> previous month
    [InlineData("2026-04-01", "2026-06-30", "2026-01-01", "2026-03-31")]   // quarter -> previous quarter
    [InlineData("2025-01-01", "2025-12-31", "2024-01-01", "2024-12-31")]   // year -> previous year
    [InlineData("2026-06-01", "2026-06-04", "2026-05-01", "2026-05-04")]   // month to date -> same days last month
    [InlineData("2026-04-01", "2026-06-04", "2026-01-01", "2026-03-06")]   // quarter to date -> same first 65 days of the previous quarter
    [InlineData("2026-01-01", "2026-06-04", "2025-01-01", "2025-06-04")]   // year to date -> same days last year
    [InlineData("2026-05-06", "2026-06-04", "2026-04-06", "2026-05-05")]   // 30 days -> the 30 days before
    public void Previous_is_the_period_of_the_same_shape_just_before(string s, string e, string ps, string pe)
    {
        var prior = Periods.Previous(new DateRange(DateOnly.Parse(s), DateOnly.Parse(e), "x"));
        Assert.Equal(DateOnly.Parse(ps), prior.Start);
        Assert.Equal(DateOnly.Parse(pe), prior.End);
    }

    [Fact]
    public void Same_period_a_year_earlier_keeps_the_dates()
    {
        var r = Periods.SameLastYear(new DateRange(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), "x"));
        Assert.Equal(new DateOnly(2025, 5, 1), r.Start);
        Assert.Equal(new DateOnly(2025, 5, 31), r.End);
    }

    [Fact]
    public void Compare_two_named_periods_puts_the_later_one_first_whatever_the_order_written()
    {
        var c = Periods.Compare("payments last month vs this month", Today)!;
        Assert.Equal(new DateOnly(2026, 6, 1), c.Current.Start);
        Assert.Equal(new DateOnly(2026, 5, 1), c.Prior.Start);
    }

    [Fact]
    public void Compare_one_period_defaults_to_the_period_before()
    {
        var c = Periods.Compare("compare payments last month", Today)!;
        Assert.Equal(new DateOnly(2026, 5, 1), c.Current.Start);
        Assert.Equal(new DateOnly(2026, 4, 1), c.Prior.Start);
    }

    [Fact]
    public void Compare_year_over_year_uses_the_same_dates_a_year_earlier()
    {
        var c = Periods.Compare("denials last quarter vs the same quarter last year", Today)!;
        Assert.Equal(new DateOnly(2026, 1, 1), c.Current.Start);
        Assert.Equal(new DateOnly(2025, 1, 1), c.Prior.Start);
        Assert.Equal(new DateOnly(2025, 3, 31), c.Prior.End);
    }

    [Fact]
    public void Compare_with_no_period_is_null() => Assert.Null(Periods.Compare("compare payments", Today));
}
