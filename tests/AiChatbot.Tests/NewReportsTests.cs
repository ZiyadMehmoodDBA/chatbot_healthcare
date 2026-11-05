using System.Text.Json;
using AiChatbot.Agent;
using AiChatbot.Agent.Tools;
using AiChatbot.Reporting;

namespace AiChatbot.Tests;

// Claim volume, days to bill, denial analysis and the patient billing balance: shapes, scope, limits and PHI handling.
public class NewReportsTests
{
    private sealed class Exec(Func<ReportQuery, IReadOnlyList<object?[]>> answer) : IReportExecutor
    {
        public List<ReportQuery> Queries { get; } = [];

        public Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery q, CancellationToken ct)
        {
            if (q.Sql.StartsWith("SELECT MAX(", StringComparison.Ordinal))
                return Task.FromResult<IReadOnlyList<object?[]>>([[new DateTime(2026, 6, 5)]]);
            Queries.Add(q);
            return Task.FromResult(answer(q));
        }
    }

    private sealed class CaptureAudit : IReportAudit
    {
        public List<ReportAuditEntry> Entries { get; } = [];
        public void Record(ReportAuditEntry entry) => Entries.Add(entry);
    }

    private static readonly DateTime Now = new(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Actor Manager = new("u1", Roles.RcmManager, [10, 11]);
    private static readonly Actor Analyst = new("u2", Roles.Analyst, [10]);
    private static readonly Actor Billing = new("u3", Roles.BillingStaff, [10]);

    private static ReportBroker Broker(Exec exec, CaptureAudit? audit = null, ReportServiceOptions? o = null)
        => new(exec, audit ?? new CaptureAudit(), o ?? new ReportServiceOptions(), () => Now);

    private static ReportRequest May => new(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31));

    private static object?[] Line(long claim, long seq) =>
        [10L, claim, seq, new DateTime(2026, 5, 1), "99213", new DateTime(2026, 5, 5), "Billed", 100m, 60m, 10m, 5m, 25m, 25m];

    // ----- claim volume -----

    [Fact]
    public async Task Claim_volume_groups_by_provider_inside_the_actors_practices()
    {
        var exec = new Exec(_ => [[10L, 77L, "Dr Able", 5L, 9L, 900m]]);
        var r = await Broker(exec).RunAsync(Manager, ReportCatalog.ClaimVolume, May, "run", "c");

        var q = Assert.Single(exec.Queries);
        Assert.Contains("reporting.vw_Rpt_ClaimVolume", q.Sql);
        Assert.Contains("DOS >= @start", q.Sql);
        Assert.Contains("Practice_Code IN (@p0, @p1)", q.Sql);
        Assert.Equal("dos", r.Evidence.DateBasis);
        Assert.Equal(new object?[] { "10", "77", "Dr Able", "5", "9", "900" }, r.Rows[0].Select(x => (object?)Convert.ToString(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        Assert.Equal(r.Columns.Count, r.Rows[0].Count);
    }

    [Fact]
    public async Task A_requested_practice_outside_the_actors_scope_is_refused_for_every_new_report()
    {
        foreach (var id in new[] { ReportCatalog.ClaimVolume, ReportCatalog.DaysToBill, ReportCatalog.Denials })
        {
            var exec = new Exec(_ => []);
            var ex = await Assert.ThrowsAsync<ReportException>(() => Broker(exec).RunAsync(
                Analyst, id, May with { PracticeCodes = [99] }, "run", "c"));
            Assert.Equal(ReportErrorKind.Forbidden, ex.Kind);
            Assert.Empty(exec.Queries);
        }
    }

    [Fact]
    public async Task A_period_longer_than_the_limit_is_rejected_for_every_new_report()
    {
        foreach (var id in new[] { ReportCatalog.ClaimVolume, ReportCatalog.DaysToBill, ReportCatalog.Denials })
        {
            var ex = await Assert.ThrowsAsync<ReportException>(() => Broker(new Exec(_ => [])).RunAsync(
                Manager, id, new ReportRequest(new DateOnly(2025, 1, 1), new DateOnly(2026, 5, 31)), "run", "c"));
            Assert.Equal(ReportErrorKind.Invalid, ex.Kind);
        }
    }

    // ----- days to bill -----

    [Fact]
    public async Task Days_to_bill_has_an_overall_first_row_then_the_payers()
    {
        var exec = new Exec(q => q.Sql.Contains("TOP (@topN)")
            ? [[7L, "Acme Health", 3L, 12.5m, 40]]
            : [[10L, 11.25m, 90]]);
        var r = await Broker(exec).RunAsync(Manager, ReportCatalog.DaysToBill, May, "run", "c");

        Assert.Equal(2, r.Rows.Count);
        Assert.Equal("All payers", r.Rows[0][1]);
        Assert.Equal(10L, r.Rows[0][2]);
        Assert.Equal(11.25m, r.Rows[0][3]);
        Assert.Equal("Acme Health", r.Rows[1][1]);
        Assert.Equal("submitted", r.Evidence.DateBasis);
        // Negative gaps and missing dates are excluded in SQL, not averaged in
        var main = exec.Queries.First(q => q.Sql.Contains("TOP (@topN)")).Sql;
        Assert.Contains("DATEDIFF(DAY, v.DOS, v.First_Submission_Date) >= 0", main);
        Assert.Contains("v.DOS IS NOT NULL", main);
        Assert.DoesNotContain("DOSAge", main);
    }

    [Fact]
    public async Task Days_to_bill_with_no_claims_returns_no_rows_not_an_overall_row_of_zeros()
    {
        var exec = new Exec(q => q.Sql.Contains("TOP (@topN)") ? [] : [[0L, 0m, 0]]);
        var r = await Broker(exec).RunAsync(Manager, ReportCatalog.DaysToBill, May, "run", "c");
        Assert.Empty(r.Rows);
    }

    // ----- denials -----

    [Fact]
    public async Task Denials_count_claims_not_dollars_and_leave_free_text_out()
    {
        var exec = new Exec(q => q.Sql.Contains("TOP (@topN)")
            ? [["D", "Not covered", 4L, 5L, 2L], ["R", "Invalid ID", 2L, 2L, 1L]]
            : [[5L, 7L, 2L]]);
        var r = await Broker(exec).RunAsync(Manager, ReportCatalog.Denials, May, "run", "c");

        Assert.Equal(3, r.Rows.Count);
        Assert.Equal("Any status", r.Rows[0][0]);
        Assert.Equal(5L, r.Rows[0][2]); // overall distinct claims; groups (4 + 2) can overlap
        Assert.Equal("status", r.Evidence.DateBasis);
        Assert.Contains(r.Evidence.Caveats, c => c.Contains("counts claims, not dollars"));
        var main = exec.Queries.First(q => q.Sql.Contains("TOP (@topN)")).Sql;
        Assert.DoesNotContain("Claim_Status_Reason", main);
        Assert.DoesNotContain("Procedure_Code", main);
    }

    // ----- model tools and placeholders -----

    [Fact]
    public async Task The_new_tools_return_prefixed_placeholders_the_model_can_use()
    {
        var exec = new Exec(q =>
            q.Sql.Contains("vw_Rpt_DaysToBill") ? (q.Sql.Contains("TOP (@topN)") ? [[7L, "Acme Health", 3L, 12.5m, 40]] : [[3L, 12.5m, 40]])
            : q.Sql.Contains("vw_Rpt_Denials") ? (q.Sql.Contains("TOP (@topN)") ? [["D", "Not covered", 4L, 5L, 2L]] : [[4L, 5L, 2L]])
            : [[10L, 77L, "Dr Able", 5L, 9L, 900m]]);
        var rcm = new RcmReportTools(Broker(exec), () => Now);
        var turn = new ChatTurn(Manager, "c");
        IDictionary<string, object?> args = new Dictionary<string, object?> { ["startDate"] = "2026-05-01", ["endDate"] = "2026-05-31" };

        var (claims, _) = await rcm.ExecuteAsync(turn, RcmReportTools.ClaimVolume, args);
        var (dtb, _) = await rcm.ExecuteAsync(turn, RcmReportTools.DaysToBill, args);
        var (denials, _) = await rcm.ExecuteAsync(turn, RcmReportTools.Denials, args);

        Assert.Equal("Dr Able", claims.Values["CLAIMS_TOP_PROVIDER"]);
        Assert.Equal("5", claims.Values["CLAIMS_TOP_CLAIMS"]);
        Assert.Equal("12.5", dtb.Values["DTB_OVERALL_AVG_DAYS"]);
        Assert.Equal("4", denials.Values["DENIAL_OVERALL_CLAIMS"]);
        // Database text (a denial reason) is not handed to the model
        Assert.DoesNotContain(denials.Values.Values, v => v.Contains("Not covered"));
    }

    // ----- patient billing balance -----

    private static Exec PatientExec(int lines = 2, bool any = true) => new(q =>
        !any ? []
        : q.Sql.Contains("TOP (@max)") ? Enumerable.Range(1, lines).Select(i => Line(555, i)).ToList()
        : [[(long)lines, 1L, 200m, 110m, 10m, 10m, 70m, 70m]]);

    [Fact]
    public async Task A_patient_lookup_returns_the_lines_then_a_total_row()
    {
        var r = await Broker(PatientExec()).RunAsync(Billing, ReportCatalog.PatientBilling,
            new ReportRequest(AccountNumber: 12345), "run", "c");

        Assert.Equal(3, r.Rows.Count);
        Assert.Equal("Total", r.Rows[2][0]);
        Assert.Equal("1 claim", r.Rows[2][1]);
        Assert.Equal(70m, r.Rows[2][12]);
        Assert.Equal("2026-05-01", r.Rows[0][3]);
        Assert.Equal("current balance", r.Evidence.Period);
        Assert.All(r.Rows, row => Assert.Equal(r.Columns.Count, row.Count));
    }

    [Fact]
    public void Patient_columns_expose_no_name_birth_date_phone_or_policy_number()
    {
        var def = ReportCatalog.Find(ReportCatalog.PatientBilling)!;
        var banned = new[] { "name", "dob", "birth", "phone", "subscriber", "policy", "address", "email", "ssn" };
        Assert.DoesNotContain(def.Columns, c => banned.Any(b => c.Name.Contains(b, StringComparison.OrdinalIgnoreCase)));

        // The view script is the contract: only the lookup key is exposed, none of the other patient columns
        var code = string.Concat(File.ReadAllLines(Script("006_vw_Rpt_PatientBilling.sql")).Where(l => !l.TrimStart().StartsWith("--")));
        foreach (var phi in new[] { "Patient_Name", "DOB", "Phone", "Subscriber", "Accession", "Diagnosis", "Email" })
            Assert.DoesNotContain(phi, code);
        Assert.Contains("Patient_Account", code);
    }

    private static string Script(string name, [System.Runtime.CompilerServices.CallerFilePath] string source = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "..", "..", "data", "reporting-views", name));

    [Fact]
    public async Task The_account_number_stays_out_of_evidence_sql_and_audit()
    {
        var audit = new CaptureAudit();
        var exec = PatientExec();
        var r = await Broker(exec, audit).RunAsync(Billing, ReportCatalog.PatientBilling,
            new ReportRequest(AccountNumber: 7654321), "run", "c");

        Assert.DoesNotContain("7654321", JsonSerializer.Serialize(r.Evidence));
        Assert.All(exec.Queries, q => Assert.DoesNotContain("7654321", q.Sql));
        Assert.Contains(exec.Queries.SelectMany(q => q.Parameters), p => p.Name == "@account" && (long)p.Value == 7654321L);
        var entry = Assert.Single(audit.Entries);
        Assert.DoesNotContain("7654321", string.Join(";", entry.Filters.Values));
        Assert.Equal(10, entry.Filters["accountRef"].Length);
        var same = new ReportBroker(null!, null!, new ReportServiceOptions());
        Assert.Equal(entry.Filters["accountRef"], same.AccountRef(7654321));
        Assert.NotEqual(same.AccountRef(7654321), same.AccountRef(7654322));
    }

    [Fact]
    public void The_account_reference_is_keyed_so_a_plain_hash_cannot_recompute_it()
    {
        var a = new ReportBroker(null!, null!, new ReportServiceOptions { AccountRefKey = "key-one" });
        var b = new ReportBroker(null!, null!, new ReportServiceOptions { AccountRefKey = "key-two" });
        var plain = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("12345")))[..10].ToLowerInvariant();

        Assert.Equal(a.AccountRef(12345), new ReportBroker(null!, null!, new ReportServiceOptions { AccountRefKey = "key-one" }).AccountRef(12345));
        Assert.NotEqual(a.AccountRef(12345), b.AccountRef(12345));
        Assert.NotEqual(plain, a.AccountRef(12345));
        Assert.NotEqual(plain, new ReportBroker(null!, null!, new ReportServiceOptions()).AccountRef(12345));
    }

    [Fact]
    public async Task An_analyst_cannot_run_the_patient_report_and_nothing_is_queried()
    {
        var exec = PatientExec();
        var ex = await Assert.ThrowsAsync<ReportException>(() => Broker(exec).RunAsync(
            Analyst, ReportCatalog.PatientBilling, new ReportRequest(AccountNumber: 12345), "run", "c"));
        Assert.Equal(ReportErrorKind.Forbidden, ex.Kind);
        Assert.Empty(exec.Queries);
    }

    [Fact]
    public async Task A_patient_lookup_needs_an_account_number()
    {
        var ex = await Assert.ThrowsAsync<ReportException>(() => Broker(PatientExec()).RunAsync(
            Billing, ReportCatalog.PatientBilling, new ReportRequest(), "run", "c"));
        Assert.Equal(ReportErrorKind.Invalid, ex.Kind);
    }

    [Fact]
    public async Task A_caller_cannot_widen_the_patient_lookup_beyond_their_practices()
    {
        var exec = PatientExec();
        await Assert.ThrowsAsync<ReportException>(() => Broker(exec).RunAsync(
            Billing, ReportCatalog.PatientBilling, new ReportRequest(AccountNumber: 12345, PracticeCodes: [99]), "run", "c"));
        Assert.Empty(exec.Queries);

        await Broker(exec).RunAsync(Billing, ReportCatalog.PatientBilling, new ReportRequest(AccountNumber: 12345), "run", "c");
        Assert.All(exec.Queries, q => Assert.Equal([10L], q.Parameters.Where(p => p.Name.StartsWith("@p")).Select(p => (long)p.Value).ToArray()));
    }

    [Fact]
    public async Task An_account_with_no_lines_returns_no_rows_and_no_total_row()
    {
        var r = await Broker(PatientExec(any: false)).RunAsync(Billing, ReportCatalog.PatientBilling,
            new ReportRequest(AccountNumber: 12345), "run", "c");
        Assert.Empty(r.Rows);
    }

    [Fact]
    public async Task A_long_account_is_cut_at_the_row_limit_and_says_so()
    {
        var o = new ReportServiceOptions { MaxPatientRows = 3 };
        var exec = new Exec(q => q.Sql.Contains("TOP (@max)")
            ? Enumerable.Range(1, 4).Select(i => Line(555, i)).ToList() // one more than the limit: truncated
            : [[9L, 1L, 900m, 0m, 0m, 0m, 0m, 0m]]);
        var r = await Broker(exec, o: o).RunAsync(Billing, ReportCatalog.PatientBilling,
            new ReportRequest(AccountNumber: 12345), "run", "c");

        Assert.True(r.Evidence.Truncated);
        Assert.Equal(4, r.Rows.Count); // 3 lines + total
        Assert.Equal(9L, r.Rows[^1][2]); // total covers every line, not just the 3 shown
        Assert.Contains("newest 3 claim lines", r.Evidence.Caveats[0]);
        Assert.Equal(4, exec.Queries.First(q => q.Sql.Contains("TOP (@max)")).Parameters.Single(p => p.Name == "@max").Value);
    }

    [Fact]
    public void The_patient_report_is_flagged_chat_only_and_all_new_views_validate()
    {
        Assert.True(ReportCatalog.Find(ReportCatalog.PatientBilling)!.PatientLevel);
        Assert.DoesNotContain(ReportCatalog.All.Where(r => r.Id != ReportCatalog.PatientBilling), r => r.PatientLevel);
        Assert.Throws<InvalidOperationException>(() => new ReportQueries(new ReportServiceOptions { DenialsView = "x; DROP TABLE y" }));
        Assert.Throws<InvalidOperationException>(() => new ReportQueries(new ReportServiceOptions { PatientBillingView = "v w" }));
        Assert.Throws<InvalidOperationException>(() => new ReportQueries(new ReportServiceOptions { ClaimVolumeView = "" }));
        Assert.Throws<InvalidOperationException>(() => new ReportQueries(new ReportServiceOptions { DaysToBillView = "a--b" }));
    }
}
