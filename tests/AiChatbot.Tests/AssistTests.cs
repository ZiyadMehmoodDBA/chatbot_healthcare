using AiChatbot.Agent.Conversation;
using AiChatbot.Agent.Tools;
using AiChatbot.Reporting;
using AiChatbot.Safety;

namespace AiChatbot.Tests;

// The scope line, the empty-result note and the chips are built in code from the report evidence.
public class AssistTests
{
    private static readonly Actor Manager = new("u1", "RcmManager", [10L, 11L]);

    private static ReportResult Table(string id, string period, DateOnly? through, params object?[][] rows)
        => new([], rows.Select(r => (IReadOnlyList<object?>)r).ToList(),
            new ReportEvidence(id, "0.1", "Draft", "src", "posting", period, new Dictionary<string, string>(), rows.Length, false,
                DateTime.UtcNow, [], through));

    [Theory]
    [InlineData("How long does it take us to bill for year 2026?")]
    [InlineData("payments in 2026")]
    [InlineData("denials in May 2026")]
    [InlineData("claims Q1 2026")]
    public void A_year_after_a_time_word_is_not_blocked(string text)
        => Assert.True(ExternalInputScreen.IsSafe(text, []));

    [Theory]
    [InlineData("show 2026")]
    [InlineData("lookup 12345")]
    [InlineData("payments in 20267")]
    [InlineData("call 555 123 4567")]
    [InlineData("born in 1980")] // a birth word still blocks
    [InlineData("patient 1980-05-02")]
    public void Other_numbers_are_still_blocked(string text)
        => Assert.False(ExternalInputScreen.IsSafe(text, []));

    [Fact]
    public void The_scope_line_states_access_period_basis_status_and_freshness()
    {
        var t = Table(ReportCatalog.InsuranceVsPatient, "2026-05-01 to 2026-05-31", new DateOnly(2026, 6, 5), [1L, 2.5m]);

        var a = AssistBuilder.ForReports(Manager, [t])!;

        Assert.Equal("2 practices in your access · 2026-05-01 to 2026-05-31 · posting · Draft · data through 2026-06-05", a.Scope);
        Assert.Null(a.Note);
    }

    [Fact]
    public void A_filled_table_gets_follow_up_chips_that_keep_the_period()
    {
        var t = Table(ReportCatalog.InsuranceVsPatient, "2026-05-01 to 2026-05-31", new DateOnly(2026, 6, 5), [1L, 2.5m]);

        var a = AssistBuilder.ForReports(Manager, [t])!;

        Assert.Contains(a.Chips, c => c.Send == "same by payer");
        Assert.All(a.Chips, c => Assert.StartsWith("same", c.Send));
    }

    [Fact]
    public void A_period_past_the_data_gets_a_note_and_the_latest_full_month_as_a_chip()
    {
        var t = Table(ReportCatalog.InsuranceVsPatient, "2026-10-01 to 2026-10-31", new DateOnly(2026, 6, 5), [0L, 0m]);

        var a = AssistBuilder.ForReports(Manager, [t])!;

        Assert.Contains("have data for 2026-10-01 to 2026-10-31 yet", a.Note);
        Assert.Contains("2026-06-05", a.Note);
        Assert.Contains(a.Chips, c => c.Send == "same for May");
    }

    [Fact]
    public void An_empty_table_inside_the_data_says_no_records()
    {
        var t = Table(ReportCatalog.Denials, "2026-05-01 to 2026-05-31", new DateOnly(2026, 6, 5));

        Assert.True(AssistBuilder.IsEmpty(t));
        Assert.StartsWith("I looked at", AssistBuilder.EmptyReply([t]));
    }

    [Fact]
    public void Zeros_count_as_empty_but_text_alone_does_not_hide_a_real_number()
    {
        Assert.True(AssistBuilder.IsEmpty(Table(ReportCatalog.Denials, "p", null, ["x", 0L, 0m])));
        Assert.False(AssistBuilder.IsEmpty(Table(ReportCatalog.Denials, "p", null, ["x", 0L, 5m])));
    }

    [Fact]
    public void A_patient_lookup_gets_no_scope_line_and_no_chips()
        => Assert.Null(AssistBuilder.ForReports(Manager, [Table(ReportCatalog.PatientBilling, "p", null, [1L])]));

    [Fact]
    public void No_report_means_no_assist()
        => Assert.Null(AssistBuilder.ForReports(Manager, []));

    [Fact]
    public void The_period_chips_are_things_the_period_reader_understands()
    {
        var today = new DateOnly(2026, 11, 5);
        Assert.All(AssistBuilder.PeriodChips, c => Assert.True(ClarifyPlanner.HasPeriod(c.Send, today), c.Send));
    }

    [Fact]
    public void The_follow_up_chips_are_not_a_new_period_question()
    {
        // "same ..." with a report on screen reuses the period, so a chip never triggers the period question again
        foreach (var chip in new[] { "same by payer", "same by practice", "same by month", "same for May", "same for year to date" })
            Assert.Matches("(?i)\\bsame\\b", chip);
    }

    [Fact]
    public void A_date_in_a_row_does_not_make_an_empty_table_look_filled()
        => Assert.True(AssistBuilder.IsEmpty(Table(ReportCatalog.InsuranceVsPatient, "p", null, [0L, 0m, new DateOnly(2026, 6, 5)])));

    [Fact]
    public void Days_to_bill_with_no_rows_says_the_source_is_empty_and_offers_submissions()
    {
        var t = Table(ReportCatalog.DaysToBill, "2026-05-01 to 2026-05-31", new DateOnly(2026, 6, 5));

        var a = AssistBuilder.ForReports(Manager, [t])!;

        Assert.Contains("source table that had no rows", a.Note);
        Assert.Equal("same submissions", a.Chips[0].Send);
    }
}
