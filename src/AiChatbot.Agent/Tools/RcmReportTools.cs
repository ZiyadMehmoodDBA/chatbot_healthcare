using System.Text.Json;
using AiChatbot.Agent.Domains;
using AiChatbot.Reporting;
using Microsoft.Extensions.AI;

namespace AiChatbot.Agent.Tools;

// The registry of RCM domain modules. The model sees tool names and schemas; scope, role checks, SQL and every
// figure stay behind each module's boundary (see Domains/DomainModule.cs).
public sealed class RcmReportTools
{
    public const string InsuranceVsPatient = "GetInsuranceVsPatientPayments";
    public const string PaymentsByPayer = "GetPaymentsByPayer";
    public const string InsuranceAging = "GetInsuranceArAging";

    private const string Dates =
        "\"startDate\":{\"type\":\"string\",\"description\":\"First day, YYYY-MM-DD\"}," +
        "\"endDate\":{\"type\":\"string\",\"description\":\"Last day, YYYY-MM-DD\"}";

    private const string TopN =
        "\"topN\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100,\"description\":\"How many payers to return, default 10\"}";

    private const string PracticeScope = "the caller's assigned practices; never chosen by the model";

    private readonly Func<DateTime> _now;
    private readonly Dictionary<string, IDomainModule> _byTool;

    public RcmReportTools(ReportBroker broker, Func<DateTime>? clock = null, ReportServiceOptions? options = null)
    {
        _now = clock ?? (() => DateTime.UtcNow);
        options ??= new ReportServiceOptions();

        ToolSpec Spec(string name, string domain, string reportId, string description, string properties, string[] required, int maxRows)
            => new(name, domain, description, Schema(properties, required),
                ReportCatalog.Find(reportId)!.AllowedRoles, PracticeScope, maxRows, options.CommandTimeoutSeconds);

        Modules =
        [
            new ReportModule(
                "payments",
                "payments received (insurance versus patient, and by payer)",
                ["payment", "payments", "paid", "collect", "collected", "collection", "collections", "remittance", "deposit", "insurance vs patient", "insurance versus patient"],
                [
                    new(Spec(InsuranceVsPatient, "payments", ReportCatalog.InsuranceVsPatient,
                        "Insurance payments versus patient payments for a posting-date period, with each share of the combined total.",
                        Dates, ["startDate", "endDate"], 1), ReportCatalog.InsuranceVsPatient, "SPLIT"),
                    new(Spec(PaymentsByPayer, "payments", ReportCatalog.PaymentsByPayer,
                        "Payers ranked by combined payments received in a posting-date period.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), ReportCatalog.PaymentsByPayer, "PAYER"),
                ],
                broker),
            new ReportModule(
                "accounts receivable",
                "insurance A/R aging (open balances by payer and age)",
                ["aging", "a/r", "receivable", "receivables", "outstanding", "open balance", "open balances", "overdue", "unpaid", "balance", "balances"],
                [
                    new(Spec(InsuranceAging, "accounts receivable", ReportCatalog.InsuranceAging,
                        "Open insurance balance by payer and age bucket (0-30, 31-60, 61-90, 91-120, over 120 days since billed).",
                        "\"asOfDate\":{\"type\":\"string\",\"description\":\"As-of day, YYYY-MM-DD; defaults to today\"}," + TopN,
                        [], options.MaxTopN), ReportCatalog.InsuranceAging, "AGING"),
                ],
                broker),
        ];
        _byTool = Modules.SelectMany(m => m.Tools.Select(t => (t.Name, Module: m))).ToDictionary(x => x.Name, x => x.Module);
    }

    public IReadOnlyList<IDomainModule> Modules { get; }

    public DateOnly Today => DateOnly.FromDateTime(_now());

    public IReadOnlyList<AITool> Declarations => Declare(Modules);

    public static IReadOnlyList<AITool> Declare(IEnumerable<IDomainModule> modules)
        => modules.SelectMany(m => m.Tools).Select(t => t.ToDeclaration()).ToList();

    private static JsonElement Schema(string properties, string[] required)
        => JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{" + properties + "},\"required\":[" +
            string.Join(",", required.Select(r => "\"" + r + "\"")) + "],\"additionalProperties\":false}").RootElement;

    public Task<(ToolResult Result, ReportResult? Report)> ExecuteAsync(
        ChatTurn turn, string name, IDictionary<string, object?>? args, CancellationToken ct = default)
        => _byTool.TryGetValue(name, out var module)
            ? module.ExecuteAsync(turn, name, args, ct)
            : Task.FromResult<(ToolResult, ReportResult?)>((ToolResult.Fail("unknown_tool"), null));
}
