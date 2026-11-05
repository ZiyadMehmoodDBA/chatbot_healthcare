using AiChatbot.Reporting;
using Xunit;

namespace AiChatbot.Tests;

public class GroupedReportsTests
{
    private sealed class Exec(params object?[][] rows) : IReportExecutor
    {
        public List<ReportQuery> Queries { get; } = [];
        public Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery q, CancellationToken ct)
        {
            Queries.Add(q);
            return Task.FromResult<IReadOnlyList<object?[]>>(q.Sql.StartsWith("SELECT MAX(", StringComparison.Ordinal)
                ? [[new DateTime(2026, 6, 5)]] : rows);
        }
    }

    private sealed class NoAudit : IReportAudit { public void Record(ReportAuditEntry e) { } }

    private static readonly Actor Manager = new("u1", Roles.RcmManager, [10, 20]);
    private static readonly ReportRequest Jan = new(StartDate: new(2026, 1, 1), EndDate: new(2026, 1, 31));

    private static (ReportBroker, Exec) Make(params object?[][] rows)
    {
        var e = new Exec(rows);
        return (new ReportBroker(e, new NoAudit(), new ReportServiceOptions(), () => new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)), e);
    }

    [Fact]
    public async Task Every_grouped_report_runs_with_actor_scope_bound_and_no_caller_text_in_sql()
    {
        foreach (var spec in GroupedReports.All)
        {
            var (b, e) = Make();
            var r = await b.RunAsync(Manager, spec.Id, Jan, "run", "c");
            Assert.Equal(spec.Id, r.Evidence.ReportId);
            var q = e.Queries[0];
            Assert.Contains("Practice_Code IN (@p0, @p1)", q.Sql);
            Assert.DoesNotContain("2026", q.Sql);
            Assert.Equal(spec.Groups.Count + spec.Measures.Count, r.Columns.Count);
        }
    }

    [Fact]
    public async Task Month_grouping_is_ordered_oldest_first_and_rows_are_typed()
    {
        var (b, e) = Make(["2026-01", 5L, 100m, 20m, 120m], ["2026-02", 6L, 10m, 0m, 10m]);
        var r = await b.RunAsync(Manager, GroupedReports.PaymentsByMonth, Jan, "run", "c");
        Assert.Contains("ORDER BY CONVERT(char(7), Posting_Date, 120)", e.Queries[0].Sql);
        Assert.Equal("2026-01", r.Rows[0][0]);
        Assert.Equal(5L, r.Rows[0][1]);
        Assert.Equal(120m, r.Rows[0][4]);
    }

    [Fact]
    public async Task Grouped_reports_carry_the_parent_open_decision_and_stay_draft()
    {
        var (b, _) = Make();
        var r = await b.RunAsync(Manager, GroupedReports.PaymentsByPractice, Jan, "run", "c");
        Assert.Equal("Draft", r.Evidence.ApprovalStatus);
        Assert.Contains(r.Evidence.Caveats, c => c.Contains("DEC-01"));
    }

    [Fact]
    public async Task Practice_scope_cannot_be_widened_by_a_grouped_request()
    {
        var (b, _) = Make();
        await Assert.ThrowsAsync<ReportException>(() =>
            b.RunAsync(Manager, GroupedReports.ClaimVolumeByPractice, Jan with { PracticeCodes = [99] }, "run", "c"));
    }

    [Fact]
    public void Every_grouped_report_has_a_registry_entry_and_can_be_compared()
    {
        foreach (var spec in GroupedReports.All)
        {
            Assert.NotNull(MetricRegistry.Default.StatusFor(spec.Id));
            Assert.True(ComparisonBuilder.CanCompare(spec.Id));
        }
    }
}
