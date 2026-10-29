using System.Globalization;
using System.Text.Json;
using AiChatbot.Agent.Tools;
using AiChatbot.Reporting;
using AiChatbot.Safety;
using Microsoft.Extensions.AI;

namespace AiChatbot.Agent.Domains;

// The tool contract (architecture section 8): every tool declares its name, input schema, who may call it, what
// data scope applies, and its limits. Validation, authorization and execution live behind this boundary, so the
// model and the coordinator only see names and schemas. Modules are in-process today; the same contract can be put
// behind an MCP transport later without changing the agent.
public sealed record ToolSpec(
    string Name,
    string Domain,
    string Description,
    JsonElement InputSchema,
    IReadOnlyList<string> AllowedRoles,
    string DataScope,
    int MaxRows,
    int TimeoutSeconds)
{
    public AITool ToDeclaration() => AIFunctionFactory.CreateDeclaration(Name, Description, InputSchema);
}

// Normalized error codes returned to the model: "code: message". Messages are fixed text, never driver detail.
public static class ToolErrors
{
    public const string InvalidArguments = "invalid_arguments";
    public const string Forbidden = "forbidden";
    public const string Unauthenticated = "unauthenticated";
    public const string NotFound = "not_found";
    public const string Unavailable = "unavailable";
    public const string NotOffered = "not_offered";
    public const string BudgetExceeded = "budget_exceeded";

    public static string From(ReportException ex) => ex.Kind switch
    {
        ReportErrorKind.Unavailable => Unavailable,
        ReportErrorKind.Forbidden => $"{Forbidden}: {ex.Message}",
        ReportErrorKind.Unauthenticated => $"{Unauthenticated}: {ex.Message}",
        ReportErrorKind.NotFound => $"{NotFound}: {ex.Message}",
        _ => $"{InvalidArguments}: {ex.Message}",
    };
}

public interface IDomainModule
{
    string Domain { get; }
    string Description { get; }

    // Lower-case words in a question that point at this domain; used by the coordinator's router
    IReadOnlyList<string> Keywords { get; }
    IReadOnlyList<ToolSpec> Tools { get; }

    bool Allows(Actor actor);

    Task<(ToolResult Result, ReportResult? Report)> ExecuteAsync(
        ChatTurn turn, string tool, IDictionary<string, object?>? args, CancellationToken ct);
}

// One report exposed as one tool. Prefix keys the {PLACEHOLDER} names in its result so two reports in one turn
// can never overwrite each other's figures.
public sealed record ReportBinding(ToolSpec Spec, string ReportId, string Prefix);

// A domain whose tools are governed reports. Practice scope, role check, SQL and figures all come from ReportBroker.
public sealed class ReportModule(
    string domain, string description, IReadOnlyList<string> keywords, IReadOnlyList<ReportBinding> bindings, ReportBroker broker)
    : IDomainModule
{
    public string Domain { get; } = domain;
    public string Description { get; } = description;
    public IReadOnlyList<string> Keywords { get; } = keywords;
    public IReadOnlyList<ToolSpec> Tools { get; } = bindings.Select(b => b.Spec).ToList();

    public bool Allows(Actor actor) => Tools.Any(t => t.AllowedRoles.Contains(actor.Role));

    public async Task<(ToolResult Result, ReportResult? Report)> ExecuteAsync(
        ChatTurn turn, string tool, IDictionary<string, object?>? args, CancellationToken ct)
    {
        var binding = bindings.FirstOrDefault(b => b.Spec.Name == tool);
        if (binding is null) return (ToolResult.Fail("unknown_tool"), null);
        try
        {
            if (!ReportArgs.TryBuild(binding.ReportId, args, out var request, out var problem))
                return (ToolResult.Fail($"{ToolErrors.InvalidArguments}: {problem}"), null);

            var report = await broker.RunAsync(turn.Actor, binding.ReportId, request, "run", turn.CorrelationId, ct);
            var n = turn.Reports.Count(r => r.Evidence.ReportId == binding.ReportId) + 1;
            var prefix = n == 1 ? binding.Prefix : binding.Prefix + n.ToString(CultureInfo.InvariantCulture);
            return (new ToolResult(DataClass.Aggregate, Headline.For(binding.ReportId, prefix, report)), report);
        }
        catch (ReportException ex)
        {
            return (ToolResult.Fail(ToolErrors.From(ex)), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (ToolResult.Fail(ToolErrors.Unavailable), null);
        }
    }
}

// Model arguments -> ReportRequest. Only dates and topN are read; anything else the model sends is ignored, so it
// cannot choose a practice, a payer filter or a date basis.
internal static class ReportArgs
{
    public static bool TryBuild(string reportId, IDictionary<string, object?>? args, out ReportRequest request, out string problem)
    {
        request = new ReportRequest();
        problem = "";
        DateOnly? start = null, end = null, asOf = null;
        if (reportId == ReportCatalog.InsuranceAging)
        {
            if (!Date(args, "asOfDate", out asOf, out problem)) return false;
        }
        else if (!Date(args, "startDate", out start, out problem) || !Date(args, "endDate", out end, out problem))
            return false;

        int? topN = null;
        if (Raw(args, "topN") is { } t)
        {
            if (!int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
            {
                problem = "topN must be a whole number";
                return false;
            }
            topN = n;
        }
        request = new ReportRequest(start, end, asOf, TopN: topN);
        return true;
    }

    private static bool Date(IDictionary<string, object?>? args, string key, out DateOnly? value, out string problem)
    {
        value = null;
        problem = "";
        if (Raw(args, key) is not { } s) return true; // the broker reports what is missing
        if (!DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            problem = $"{key} must be a date written YYYY-MM-DD";
            return false;
        }
        value = d;
        return true;
    }

    private static string? Raw(IDictionary<string, object?>? args, string key)
    {
        if (args is null || !args.TryGetValue(key, out var v) || v is null) return null;
        if (v is JsonElement { ValueKind: JsonValueKind.Null }) return null;
        return v is JsonElement e ? e.ToString() : Convert.ToString(v, CultureInfo.InvariantCulture);
    }
}

// Short, formatted figures keyed by the {PLACEHOLDER} the model writes. The full table goes to the UI separately.
// Formats match what the server prints, so the invented-number check compares like with like.
internal static class Headline
{
    public static Dictionary<string, string> For(string reportId, string p, ReportResult r)
    {
        var v = new Dictionary<string, string>
        {
            [$"{p}_PERIOD"] = r.Evidence.Period,
            [$"{p}_STATUS"] = r.Evidence.ApprovalStatus,
            [$"{p}_DATA_THROUGH"] = r.Evidence.DataThrough?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown",
        };
        var rows = r.Rows;
        switch (reportId)
        {
            case ReportCatalog.InsuranceVsPatient when rows.Count > 0:
                var x = rows[0];
                v[$"{p}_UNIQUE_CLAIMS"] = Num(x[0]);
                v[$"{p}_INSURANCE_TOTAL"] = Money(x[2]);
                v[$"{p}_PATIENT_TOTAL"] = Money(x[3]);
                v[$"{p}_COMBINED_TOTAL"] = Money(x[4]);
                v[$"{p}_INSURANCE_PCT"] = Pct(x[6]);
                v[$"{p}_PATIENT_PCT"] = Pct(x[7]);
                break;
            case ReportCatalog.PaymentsByPayer:
                v[$"{p}_ROWS"] = rows.Count.ToString(CultureInfo.InvariantCulture);
                if (rows.Count > 0)
                {
                    v[$"{p}_TOP_NAME"] = Convert.ToString(rows[0][1], CultureInfo.InvariantCulture) ?? "";
                    v[$"{p}_TOP_COMBINED"] = Money(rows[0][5]);
                    v[$"{p}_LISTED_TOTAL"] = Money(rows.Sum(row => Convert.ToDecimal(row[5], CultureInfo.InvariantCulture)));
                }
                break;
            case ReportCatalog.InsuranceAging:
                v[$"{p}_ROWS"] = rows.Count.ToString(CultureInfo.InvariantCulture);
                if (rows.Count > 0)
                {
                    v[$"{p}_TOP_NAME"] = Convert.ToString(rows[0][1], CultureInfo.InvariantCulture) ?? "";
                    v[$"{p}_TOP_BALANCE"] = Money(rows[0][9]);
                    v[$"{p}_LISTED_TOTAL"] = Money(rows.Sum(row => Convert.ToDecimal(row[9], CultureInfo.InvariantCulture)));
                }
                break;
        }
        if (rows.Count == 0 || (reportId == ReportCatalog.InsuranceVsPatient && Convert.ToInt64(rows[0][1], CultureInfo.InvariantCulture) == 0))
            v[$"{p}_EMPTY"] = "No matching records in this period";
        return v;
    }

    private static string Money(object? o) => Convert.ToDecimal(o, CultureInfo.InvariantCulture).ToString("N2", CultureInfo.InvariantCulture);
    private static string Num(object? o) => Convert.ToInt64(o, CultureInfo.InvariantCulture).ToString("N0", CultureInfo.InvariantCulture);
    private static string Pct(object? o) => o is null ? "n/a" : Convert.ToDecimal(o, CultureInfo.InvariantCulture).ToString("0.##", CultureInfo.InvariantCulture) + "%";
}
