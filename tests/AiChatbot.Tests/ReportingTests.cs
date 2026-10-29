using AiChatbot.Reporting;

namespace AiChatbot.Tests;

public class ReportingTests
{
    private sealed class FakeExecutor(params object?[][] rows) : IReportExecutor
    {
        public List<ReportQuery> Queries { get; } = [];
        public Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery query, CancellationToken ct)
        {
            Queries.Add(query);
            if (query.Sql.StartsWith("SELECT MAX(", StringComparison.Ordinal))
                return Task.FromResult<IReadOnlyList<object?[]>>([[new DateTime(2026, 6, 5)]]);
            return Task.FromResult<IReadOnlyList<object?[]>>(rows);
        }
    }

    private sealed class FakeAudit : IReportAudit
    {
        public List<ReportAuditEntry> Entries { get; } = [];
        public void Record(ReportAuditEntry e) => Entries.Add(e);
    }

    private static readonly Actor Manager = new("u1", Roles.RcmManager, [10, 20]);
    private static readonly ReportRequest January = new(StartDate: new(2026, 1, 1), EndDate: new(2026, 1, 31));

    private static (ReportBroker Broker, FakeExecutor Exec, FakeAudit Audit) Make(params object?[][] rows)
    {
        var exec = new FakeExecutor(rows);
        var audit = new FakeAudit();
        return (new ReportBroker(exec, audit, new ReportServiceOptions(), () => new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)), exec, audit);
    }

    private static Task<ReportResult> Run(ReportBroker b, Actor? a, string id, ReportRequest r, string action = "run")
        => b.RunAsync(a, id, r, action, "corr-1");

    // ----- authorization -----

    [Fact]
    public async Task No_actor_is_refused_and_audited()
    {
        var (b, exec, audit) = Make();
        var ex = await Assert.ThrowsAsync<ReportException>(() => Run(b, null, ReportCatalog.InsuranceVsPatient, January));
        Assert.Equal(ReportErrorKind.Unauthenticated, ex.Kind);
        Assert.Empty(exec.Queries);
        Assert.Equal("Unauthenticated", Assert.Single(audit.Entries).Outcome);
    }

    [Fact]
    public async Task Unknown_role_is_forbidden()
    {
        var (b, exec, _) = Make();
        var ex = await Assert.ThrowsAsync<ReportException>(
            () => Run(b, new Actor("u", "Intern", [10]), ReportCatalog.InsuranceVsPatient, January));
        Assert.Equal(ReportErrorKind.Forbidden, ex.Kind);
        Assert.Empty(exec.Queries);
    }

    [Fact]
    public async Task Actor_without_practices_is_forbidden()
    {
        var (b, exec, _) = Make();
        var ex = await Assert.ThrowsAsync<ReportException>(
            () => Run(b, new Actor("u", Roles.Analyst, []), ReportCatalog.InsuranceVsPatient, January));
        Assert.Equal(ReportErrorKind.Forbidden, ex.Kind);
        Assert.Empty(exec.Queries);
    }

    [Fact]
    public async Task Requested_practice_outside_scope_is_forbidden_not_dropped()
    {
        var (b, exec, _) = Make();
        var ex = await Assert.ThrowsAsync<ReportException>(
            () => Run(b, Manager, ReportCatalog.PaymentsByPayer, January with { PracticeCodes = [10, 99] }));
        Assert.Equal(ReportErrorKind.Forbidden, ex.Kind);
        Assert.Empty(exec.Queries);
    }

    [Fact]
    public async Task Query_is_scoped_to_actor_practices_by_parameter()
    {
        var (b, exec, _) = Make([1L, 2L, 3m, 4m]);
        await Run(b, Manager, ReportCatalog.InsuranceVsPatient, January);
        var q = Assert.Single(exec.Queries, x => !x.Sql.StartsWith("SELECT MAX("));
        Assert.Contains("Practice_Code IN (@p0, @p1)", q.Sql);
        Assert.Equal([10L, 20L], q.Parameters.Where(p => p.Name.StartsWith("@p") && char.IsDigit(p.Name[2])).Select(p => (long)p.Value));
    }

    [Fact]
    public async Task Narrowing_to_one_allowed_practice_works()
    {
        var (b, exec, _) = Make([1L, 2L, 3m, 4m]);
        await Run(b, Manager, ReportCatalog.InsuranceVsPatient, January with { PracticeCodes = [20] });
        Assert.Contains("Practice_Code IN (@p0)", exec.Queries[0].Sql);
    }

    // ----- no caller text reaches SQL -----

    [Fact]
    public async Task Injection_in_date_basis_is_rejected_before_any_query()
    {
        var (b, exec, _) = Make();
        var ex = await Assert.ThrowsAsync<ReportException>(
            () => Run(b, Manager, ReportCatalog.InsuranceVsPatient, January with { DateBasis = "Posting_Date; DROP TABLE x" }));
        Assert.Equal(ReportErrorKind.Invalid, ex.Kind);
        Assert.Empty(exec.Queries);
    }

    [Fact]
    public async Task Filter_values_are_parameters_not_sql_text()
    {
        var (b, exec, _) = Make([1L, 2L, 3m, 4m]);
        await Run(b, Manager, ReportCatalog.InsuranceVsPatient, January with { PayerId = 777, ProviderId = 888 });
        var q = exec.Queries[0];
        Assert.DoesNotContain("777", q.Sql);
        Assert.DoesNotContain("888", q.Sql);
        Assert.Contains("Payer_Id = @payerId", q.Sql);
        Assert.Contains(q.Parameters, p => p.Name == "@payerId" && (long)p.Value == 777);
    }

    [Theory]
    [InlineData("reporting.vw_Rpt_Payments")]
    [InlineData("vw_Rpt_Payments")]
    public void Plain_view_names_accepted(string name) => Assert.Equal(name, ReportQueries.ValidateViewName(name, "x"));

    [Theory]
    [InlineData("v; DROP TABLE x")]
    [InlineData("v WHERE 1=1 --")]
    [InlineData("")]
    [InlineData(null)]
    public void Hostile_view_names_rejected(string? name)
        => Assert.Throws<InvalidOperationException>(() => ReportQueries.ValidateViewName(name, "x"));

    [Fact]
    public void Report_sql_never_selects_patient_identifying_columns()
    {
        var q = new ReportQueries(new ReportServiceOptions());
        var o = new ReportServiceOptions();
        var all = new[]
        {
            q.InsuranceVsPatient(ReportCatalog.Posting, new(2026, 1, 1), new(2026, 1, 2), [1], null, null, o).Sql,
            q.PaymentsByPayer(ReportCatalog.Posting, new(2026, 1, 1), new(2026, 1, 2), [1], null, 10, o).Sql,
            q.InsuranceAging(new(2026, 1, 2), [1], null, 10, o).Sql,
        };
        foreach (var sql in all)
            foreach (var banned in new[] { "Patient_Name", "Patient_Account", "Date_of_Birth", "Subscriber", "Comments", "Status_Reason" })
                Assert.DoesNotContain(banned, sql, StringComparison.OrdinalIgnoreCase);
    }

    // ----- validation -----

    [Fact]
    public async Task Missing_dates_end_before_start_and_long_ranges_are_invalid()
    {
        var (b, _, _) = Make();
        foreach (var bad in new[]
        {
            new ReportRequest(),
            January with { EndDate = new DateOnly(2025, 12, 1) },
            new ReportRequest(StartDate: new(2024, 1, 1), EndDate: new(2026, 1, 1)),
        })
        {
            var ex = await Assert.ThrowsAsync<ReportException>(() => Run(b, Manager, ReportCatalog.InsuranceVsPatient, bad));
            Assert.Equal(ReportErrorKind.Invalid, ex.Kind);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public async Task TopN_out_of_bounds_is_invalid(int topN)
    {
        var (b, _, _) = Make();
        var ex = await Assert.ThrowsAsync<ReportException>(
            () => Run(b, Manager, ReportCatalog.PaymentsByPayer, January with { TopN = topN }));
        Assert.Equal(ReportErrorKind.Invalid, ex.Kind);
    }

    [Fact]
    public async Task Unknown_report_is_not_found()
    {
        var (b, _, _) = Make();
        var ex = await Assert.ThrowsAsync<ReportException>(() => Run(b, Manager, "nope", January));
        Assert.Equal(ReportErrorKind.NotFound, ex.Kind);
    }

    [Fact]
    public async Task Aging_rejects_posting_date_basis()
    {
        var (b, _, _) = Make();
        var ex = await Assert.ThrowsAsync<ReportException>(
            () => Run(b, Manager, ReportCatalog.InsuranceAging, new ReportRequest(DateBasis: "posting")));
        Assert.Equal(ReportErrorKind.Invalid, ex.Kind);
    }

    // ----- calculation done in code -----

    [Fact]
    public async Task Insurance_vs_patient_computes_shares_in_decimal()
    {
        var (b, _, _) = Make([5L, 9L, 750.50m, 249.50m]);
        var r = await Run(b, Manager, ReportCatalog.InsuranceVsPatient, January);
        var row = Assert.Single(r.Rows);
        Assert.Equal([5L, 9L, 750.50m, 249.50m, 1000.00m, 501.00m, 75.05m, 24.95m], row);
        Assert.Equal(1, r.Evidence.RowCount);
        Assert.Equal("posting", r.Evidence.DateBasis);
        Assert.Equal("Draft", r.Evidence.ApprovalStatus);
        Assert.Equal("2026-01-01 to 2026-01-31", r.Evidence.Period);
    }

    [Fact]
    public async Task Zero_combined_gives_null_percentages_not_a_crash()
    {
        var (b, _, _) = Make([0L, 0L, 0m, 0m]);
        var r = await Run(b, Manager, ReportCatalog.InsuranceVsPatient, January);
        var row = r.Rows[0];
        Assert.Null(row[6]);
        Assert.Null(row[7]);
    }

    [Fact]
    public void Rounding_is_half_away_from_zero()
        => Assert.Equal(33.33m, ReportBroker.Pct(1m, 3m));

    [Fact]
    public async Task Payments_by_payer_adds_combined_column()
    {
        var (b, exec, _) = Make([7L, "Acme Health", 3L, 100m, 20m], [8L, "Beta", 1L, 5m, 0m]);
        var r = await Run(b, Manager, ReportCatalog.PaymentsByPayer, January with { TopN = 2 });
        Assert.Equal("120.00", ((decimal)r.Rows[0][5]!).ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(2, r.Rows.Count);
        Assert.Contains(exec.Queries[0].Parameters, p => p.Name == "@topN" && (int)p.Value == 2);
    }

    [Fact]
    public async Task Aging_totals_buckets_and_uses_as_of_default_from_clock()
    {
        var (b, exec, _) = Make([7L, "Acme", 4L, 10m, 20m, 30m, 40m, 50m, 5m]);
        var r = await Run(b, Manager, ReportCatalog.InsuranceAging, new ReportRequest());
        Assert.Equal(155m, r.Rows[0][9]);
        Assert.Equal("as of 2026-02-15", r.Evidence.Period);
        Assert.Contains(exec.Queries[0].Parameters, p => p.Name == "@asOf" && (DateTime)p.Value == new DateTime(2026, 2, 15));
    }

    [Fact]
    public async Task Extra_rows_beyond_the_limit_are_flagged_truncated()
    {
        var rows = Enumerable.Range(0, 3).Select(i => new object?[] { (long)i, $"P{i}", 1L, 1m, 1m }).ToArray();
        var (b, _, _) = Make(rows);
        var r = await Run(b, Manager, ReportCatalog.PaymentsByPayer, January with { TopN = 2 });
        Assert.True(r.Evidence.Truncated);
        Assert.Equal(2, r.Rows.Count);
    }

    // ----- audit -----

    [Fact]
    public async Task Success_is_audited_with_filters_and_no_rows()
    {
        var (b, _, audit) = Make([1L, 2L, 3m, 4m]);
        await Run(b, Manager, ReportCatalog.InsuranceVsPatient, January, "export");
        var e = Assert.Single(audit.Entries);
        Assert.Equal("export", e.Action);
        Assert.Equal("u1", e.UserId);
        Assert.Equal("ok", e.Outcome);
        Assert.Equal("corr-1", e.CorrelationId);
        Assert.Equal("10,20", e.Filters["practices"]);
    }

    // ----- export -----

    [Fact]
    public async Task Csv_neutralizes_formula_cells_in_text_columns_but_not_negative_numbers()
    {
        var (b, _, _) = Make([7L, "=HYPERLINK(\"x\")", 1L, -5m, 0m]);
        var r = await Run(b, Manager, ReportCatalog.PaymentsByPayer, January);
        var csv = CsvExport.ToCsv(r);
        Assert.Contains("'=HYPERLINK", csv);
        Assert.Contains(",-5,", csv);
        Assert.Contains("Report: payments-by-payer v0.1-draft (Draft)", csv);
    }

    // ----- catalog -----

    [Fact]
    public void Catalog_has_three_reports_with_unique_ids_and_caveats()
    {
        Assert.Equal(3, ReportCatalog.All.Count);
        Assert.Equal(3, ReportCatalog.All.Select(r => r.Id).Distinct().Count());
        Assert.All(ReportCatalog.All, r => Assert.NotEmpty(r.Caveats));
    }

    [Fact]
    public void Catalog_lists_only_reports_the_role_may_run()
    {
        var (b, _, _) = Make();
        Assert.Equal(3, b.CatalogFor(Manager).Count);
        Assert.Empty(b.CatalogFor(new Actor("u", "Intern", [1])));
    }
}
