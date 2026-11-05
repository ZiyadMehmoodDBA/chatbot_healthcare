using System.Text.Json;
using AiChatbot.Agent;
using AiChatbot.Agent.Domains;
using AiChatbot.Agent.Tools;
using AiChatbot.Reporting;

namespace AiChatbot.Tests;

// Phase 5 starter: a reviewed set of questions with the expected route, plus attempts to widen scope.
// All offline and deterministic, so it can gate every build and every model or prompt change.
public class GoldenEvalTests
{
    private sealed record Case(string Q, string Role, string Kind, string[] Domains);

    private sealed class CaptureAudit : IReportAudit
    {
        public List<ReportAuditEntry> Entries { get; } = [];
        public void Record(ReportAuditEntry entry) => Entries.Add(entry);
    }

    private sealed class OneRowExecutor : IReportExecutor
    {
        public List<ReportQuery> Queries { get; } = [];

        public Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery query, CancellationToken ct)
        {
            if (query.Sql.StartsWith("SELECT MAX(", StringComparison.Ordinal))
                return Task.FromResult<IReadOnlyList<object?[]>>([[new DateTime(2026, 6, 5)]]);
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<object?[]>>([[7L, "Acme Health", 3L, 100m, 20m]]);
        }
    }

    private static readonly DateTime Now = new(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);

    private static RcmReportTools Registry(IReportExecutor exec, IReportAudit audit)
        => new(new ReportBroker(exec, audit, new ReportServiceOptions(), () => Now), () => Now);

    public static IEnumerable<object[]> RoutingCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Golden", "coordinator.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
            yield return
            [
                c.GetProperty("q").GetString()!, c.GetProperty("role").GetString()!, c.GetProperty("kind").GetString()!,
                c.GetProperty("domains").EnumerateArray().Select(d => d.GetString()!).ToArray(),
            ];
    }

    [Theory]
    [MemberData(nameof(RoutingCases))]
    public void Routing_matches_the_golden_set(string question, string role, string kind, string[] domains)
    {
        var rcm = Registry(new OneRowExecutor(), new CaptureAudit());
        var plan = Coordinator.Plan(question, new Actor("u", role, [10]), rcm.Modules);

        Assert.Equal(kind, plan.Kind.ToString());
        // Open plans offer every allowed domain; the file lists none for them
        if (kind != "Open") Assert.Equal(domains, plan.Domains);
        if (kind == "Unsupported") Assert.Empty(plan.ToolNames);
    }

    [Fact]
    public void The_golden_set_covers_every_plan_kind()
    {
        var kinds = RoutingCases().Select(c => (string)c[2]).Distinct().Order().ToArray();
        Assert.Equal(["Multi", "Open", "Patient", "Single", "Unsupported"], kinds);
    }

    // The model must not be able to choose a practice, however it words the arguments
    [Theory]
    [InlineData("practiceCodes", "99")]
    [InlineData("practiceCode", "99")]
    [InlineData("practice", "99")]
    [InlineData("practices", "[99]")]
    [InlineData("userId", "someone-else")]
    [InlineData("role", "RcmManager")]
    [InlineData("sql", "SELECT * FROM patients")]
    [InlineData("dateBasis", "Deposit")]
    public async Task Extra_arguments_never_change_scope_or_the_query(string key, string value)
    {
        var exec = new OneRowExecutor();
        var audit = new CaptureAudit();
        var rcm = Registry(exec, audit);
        var turn = new ChatTurn(new Actor("u1", Roles.RcmManager, [10]), "corr");
        IDictionary<string, object?> args = new Dictionary<string, object?>
        {
            ["startDate"] = "2026-05-01", ["endDate"] = "2026-05-31", [key] = value,
        };

        await rcm.ExecuteAsync(turn, RcmReportTools.PaymentsByPayer, args, default);

        var q = Assert.Single(exec.Queries);
        Assert.DoesNotContain("99", q.Parameters.Select(x => x.Value?.ToString()));
        Assert.DoesNotContain("patients", q.Sql, StringComparison.OrdinalIgnoreCase);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal("10", entry.Filters["practices"]);
        Assert.Equal("u1", entry.UserId);
    }

    [Fact]
    public async Task An_unknown_tool_name_runs_nothing()
    {
        var exec = new OneRowExecutor();
        var rcm = Registry(exec, new CaptureAudit());
        var turn = new ChatTurn(new Actor("u1", Roles.RcmManager, [10]), "corr");

        var (result, report) = await rcm.ExecuteAsync(turn, "RunSql", new Dictionary<string, object?> { ["sql"] = "DROP TABLE x" }, default);

        Assert.Null(report);
        Assert.Empty(exec.Queries);
        Assert.Equal("unknown_tool", result.Error);
    }
}
