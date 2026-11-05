using AiChatbot.Reporting;
using Xunit;

namespace AiChatbot.Tests;

public class ComparisonBuilderTests
{
    private static ReportEvidence Evidence(string period, DateOnly through)
        => new(ReportCatalog.PaymentsByPayer, "0.1-draft", "Draft", "src", "posting", period,
            new Dictionary<string, string>(), 2, false, DateTime.UtcNow, ["Draft definition."], through);

    private static ReportResult Result(string period, params object?[][] rows)
    {
        var def = ReportCatalog.Find(ReportCatalog.PaymentsByPayer)!;
        return new ReportResult(def.Columns, rows.Select(r => (IReadOnlyList<object?>)r).ToList(), Evidence(period, new DateOnly(2026, 6, 5)));
    }

    // payerId, payerName, claimCount, insurancePayments, patientPayments, combinedPayments
    private static readonly object?[] OverallCur = ["", "All payers", 10L, 900m, 100m, 1000m];
    private static readonly object?[] OverallPri = ["", "All payers", 8L, 700m, 100m, 800m];

    [Fact]
    public void Rows_are_matched_on_their_text_columns_and_changes_are_computed_in_decimal()
    {
        var cur = Result("2026-05", OverallCur, ["1", "Acme", 6L, 600m, 0m, 600m]);
        var pri = Result("2026-04", OverallPri, ["1", "Acme", 4L, 400m, 0m, 400m]);

        var r = ComparisonBuilder.Build(cur, pri);

        var names = r.Columns.Select(c => c.Name).ToList();
        var acme = r.Rows.Single(x => (string?)x[1] == "Acme");
        Assert.Equal(600m, acme[names.IndexOf("combinedPayments")]);
        Assert.Equal(400m, acme[names.IndexOf("combinedPaymentsPrior")]);
        Assert.Equal(200m, acme[names.IndexOf("combinedPaymentsChange")]);
        Assert.Equal(50.00m, acme[names.IndexOf("combinedPaymentsChangePct")]);
    }

    [Fact]
    public void A_row_in_only_one_period_shows_blank_for_the_other_and_no_percent()
    {
        var cur = Result("2026-05", OverallCur, ["1", "Acme", 6L, 600m, 0m, 600m]);
        var pri = Result("2026-04", OverallPri, ["2", "Beta", 3L, 300m, 0m, 300m]);

        var r = ComparisonBuilder.Build(cur, pri);
        var names = r.Columns.Select(c => c.Name).ToList();

        var acme = r.Rows.Single(x => (string?)x[1] == "Acme");
        Assert.Null(acme[names.IndexOf("combinedPaymentsPrior")]);
        Assert.Null(acme[names.IndexOf("combinedPaymentsChangePct")]);
        Assert.Equal(600m, acme[names.IndexOf("combinedPaymentsChange")]);
        var beta = r.Rows.Single(x => (string?)x[1] == "Beta");
        Assert.Null(beta[names.IndexOf("combinedPayments")]);
        Assert.Equal(-300m, beta[names.IndexOf("combinedPaymentsChange")]);
    }

    [Fact]
    public void Evidence_names_both_periods_marks_the_table_and_keeps_every_caveat()
    {
        var r = ComparisonBuilder.Build(Result("May", OverallCur), Result("April", OverallPri), ["Different lengths."]);

        Assert.Equal("May vs April", r.Evidence.Period);
        Assert.Equal("true", r.Evidence.Filters["comparison"]);
        Assert.Equal("Draft", r.Evidence.ApprovalStatus);
        Assert.Contains("Draft definition.", r.Evidence.Caveats);
        Assert.Contains("Different lengths.", r.Evidence.Caveats);
        Assert.Contains(r.Evidence.Caveats, c => c.StartsWith("Comparison:"));
    }

    [Fact]
    public void A_period_past_the_loaded_data_is_flagged_incomplete()
    {
        var r = ComparisonBuilder.Build(Result("May", OverallCur), Result("April", OverallPri), null, "current");

        Assert.Equal("current", r.Evidence.Filters["incomplete"]);
        Assert.Contains(r.Evidence.Caveats, c => c.Contains("current period runs past the data-through date"));
    }

    [Fact]
    public void Only_selected_measures_are_compared_so_wide_reports_stay_readable()
    {
        var r = ComparisonBuilder.Build(Result("a", OverallCur), Result("b", OverallPri));
        var names = r.Columns.Select(c => c.Name).ToList();
        Assert.DoesNotContain("insurancePayments", names);
        Assert.Contains("claimCountChange", names);
    }

    [Fact]
    public void A_report_without_a_comparison_definition_is_refused()
    {
        var def = ReportCatalog.Find(ReportCatalog.PatientBilling)!;
        var res = new ReportResult(def.Columns, [], Evidence("x", new DateOnly(2026, 6, 5)) with { ReportId = ReportCatalog.PatientBilling });
        Assert.Throws<ReportException>(() => ComparisonBuilder.Build(res, res));
        Assert.False(ComparisonBuilder.CanCompare(ReportCatalog.PatientBilling));
    }
}
