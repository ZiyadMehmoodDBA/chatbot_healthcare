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
    public const string ClaimVolume = "GetClaimVolume";
    public const string DaysToBill = "GetDaysToBill";
    public const string Denials = "GetDenialAnalysis";
    public const string PaymentsByPractice = "GetPaymentsByPractice";
    public const string PaymentsByMonth = "GetPaymentsByMonth";
    public const string ClaimVolumeByPractice = "GetClaimVolumeByPractice";
    public const string ClaimVolumeByMonth = "GetClaimVolumeByMonth";
    public const string DenialsByPayer = "GetDenialsByPayer";
    public const string DenialsByMonth = "GetDenialsByMonth";
    public const string SubmissionsByType = "GetSubmissionsByType";
    public const string SubmissionsByMonth = "GetSubmissionsByMonth";
    public const string ResubmissionsByPayer = "GetResubmissionsByPayer";
    public const string ResubmissionsByMonth = "GetResubmissionsByMonth";
    public const string RemarksByCode = "GetPaymentRemarkCodes";
    public const string FinancialsByPractice = "GetChargesPaymentsByPractice";
    public const string FinancialsByMonth = "GetChargesPaymentsByMonth";

    private const string Dates =
        "\"startDate\":{\"type\":\"string\",\"description\":\"First day, YYYY-MM-DD\"}," +
        "\"endDate\":{\"type\":\"string\",\"description\":\"Last day, YYYY-MM-DD\"}";

    private const string TopN =
        "\"topN\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100,\"description\":\"How many rows to return, default 10\"}";

    private const string PracticeScope = "the caller's assigned practices; never chosen by the model";

    private readonly Func<DateTime> _now;
    private readonly ReportBroker _broker;
    private readonly Dictionary<string, IDomainModule> _byTool;

    public RcmReportTools(ReportBroker broker, Func<DateTime>? clock = null, ReportServiceOptions? options = null)
    {
        _now = clock ?? (() => DateTime.UtcNow);
        _broker = broker;
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
                    new(Spec(PaymentsByPractice, "payments", GroupedReports.PaymentsByPractice,
                        "Payments received in a posting-date period, by practice, largest first.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.PaymentsByPractice, "PRACTICE_PAY"),
                    new(Spec(PaymentsByMonth, "payments", GroupedReports.PaymentsByMonth,
                        "Payments received by posting month, oldest first. Use for trends over several months.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.PaymentsByMonth, "MONTH_PAY"),
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
            new ReportModule(
                "claims",
                "claim volume by practice and provider, and days to bill",
                ["claim volume", "claims volume", "how many claims", "number of claims", "claim count", "claims by provider", "claims by practice",
                 "claims submitted", "days to bill", "time to bill", "billing lag", "billing speed", "slow to bill"],
                [
                    new(Spec(ClaimVolume, "claims", ReportCatalog.ClaimVolume,
                        "Unique claims, claim lines and charges by practice and rendering provider for a date-of-service period.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), ReportCatalog.ClaimVolume, "CLAIMS"),
                    new(Spec(ClaimVolumeByPractice, "claims", GroupedReports.ClaimVolumeByPractice,
                        "Unique claims, lines and charges by practice for a date-of-service period.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.ClaimVolumeByPractice, "CLAIMS_PRACTICE"),
                    new(Spec(ClaimVolumeByMonth, "claims", GroupedReports.ClaimVolumeByMonth,
                        "Unique claims, lines and charges by month of service, oldest first. Use for trends.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.ClaimVolumeByMonth, "CLAIMS_MONTH"),
                    new(Spec(DaysToBill, "claims", ReportCatalog.DaysToBill,
                        "Average and longest days from date of service to first submission, overall and by payer, for claims first submitted in the period.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), ReportCatalog.DaysToBill, "DTB"),
                ],
                broker),
            new ReportModule(
                "charges",
                "charges, amounts paid and adjustments by practice and by month",
                ["charges", "charge", "adjustments", "adjustment", "adjusted", "write off", "write offs", "writeoff", "writeoffs", "billed amount", "gross charges"],
                [
                    new(Spec(FinancialsByPractice, "charges", GroupedReports.FinancialsByPractice,
                        "Charges, amount paid, amount adjusted and withheld by practice for a date-of-service period. Raw sums only.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.FinancialsByPractice, "FIN_PRACTICE"),
                    new(Spec(FinancialsByMonth, "charges", GroupedReports.FinancialsByMonth,
                        "Charges, amount paid, amount adjusted and withheld by month of service, oldest first. Use for trends.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.FinancialsByMonth, "FIN_MONTH"),
                ],
                broker),
            new ReportModule(
                "submissions",
                "claim submissions, resubmissions and payment remark codes",
                ["submission", "submissions", "resubmission", "resubmissions", "resubmitted", "resubmit", "remark code", "remark codes", "remarks", "remark"],
                [
                    new(Spec(SubmissionsByType, "submissions", GroupedReports.SubmissionsByType,
                        "Claims submitted in a period by submission type and result (accepted or rejected).",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.SubmissionsByType, "SUB_TYPE"),
                    new(Spec(SubmissionsByMonth, "submissions", GroupedReports.SubmissionsByMonth,
                        "Claims submitted by month and result, oldest first. Use for trends.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.SubmissionsByMonth, "SUB_MONTH"),
                    new(Spec(ResubmissionsByPayer, "submissions", GroupedReports.ResubmissionsByPayer,
                        "Submissions by payer in a period with how many claims were sent again.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.ResubmissionsByPayer, "RESUB_PAYER"),
                    new(Spec(ResubmissionsByMonth, "submissions", GroupedReports.ResubmissionsByMonth,
                        "Submissions by month with how many claims were sent again, oldest first.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.ResubmissionsByMonth, "RESUB_MONTH"),
                    new(Spec(RemarksByCode, "submissions", GroupedReports.RemarksByCode,
                        "Claims by remittance remark code in a date-of-payment period. Codes are shown as stored, not decoded.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.RemarksByCode, "REMARK"),
                ],
                broker),
            new ReportModule(
                "denials",
                "denials and rejections (claim counts by status and reason, not dollars)",
                ["denial", "denials", "denied", "deny", "rejection", "rejections", "rejected", "denial reasons"],
                [
                    new(Spec(Denials, "denials", ReportCatalog.Denials,
                        "Claims with a denial or rejection status entry in the period, overall and by status and reason. Counts claims, never dollars.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), ReportCatalog.Denials, "DENIAL"),
                    new(Spec(DenialsByPayer, "denials", GroupedReports.DenialsByPayer,
                        "Claims with a claim-status entry by payer and status in the period. Counts claims, never dollars.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.DenialsByPayer, "DENIAL_PAYER"),
                    new(Spec(DenialsByMonth, "denials", GroupedReports.DenialsByMonth,
                        "Claims with a claim-status entry by month and status, oldest first. Counts claims, never dollars.",
                        Dates + "," + TopN, ["startDate", "endDate"], options.MaxTopN), GroupedReports.DenialsByMonth, "DENIAL_MONTH"),
                ],
                broker),
        ];
        _byTool = Modules.SelectMany(m => m.Tools.Select(t => (t.Name, Module: m))).ToDictionary(x => x.Name, x => x.Module);
    }

    public IReadOnlyList<IDomainModule> Modules { get; }

    public DateOnly Today => DateOnly.FromDateTime(_now());

    public DateTime UtcNow => _now();

    public int MaxTopN => _broker.MaxTopN;

    // A period comparison. Not a model tool: the server runs both periods through the broker. Error text is fixed.
    public async Task<(ReportResult? Report, string? Error)> RunComparisonAsync(
        ChatTurn turn, string reportId, ReportRequest current, ReportRequest prior, IReadOnlyList<string> caveats,
        CancellationToken ct = default)
    {
        try
        {
            var report = await _broker.CompareAsync(turn.Actor, reportId, current, prior, "run", turn.CorrelationId, caveats, ct);
            return (report, null);
        }
        catch (ReportException ex)
        {
            return (null, ex.Kind switch
            {
                ReportErrorKind.Forbidden => "Your role isn't allowed to run that report.",
                ReportErrorKind.Unauthenticated => "Please sign in again.",
                ReportErrorKind.Invalid => ex.Message,
                _ => "I couldn't reach the reporting data just now, so I can't compare those periods. Please try again shortly.",
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, "I couldn't reach the reporting data just now, so I can't compare those periods. Please try again shortly.");
        }
    }

    // The patient account lookup. Not a model tool: the server calls it after the decision engine has an account.
    // Scope, role check, fixed SQL and audit all happen in the broker. Error text is fixed and safe to show the user.
    public async Task<(ReportResult? Report, string? Error)> RunPatientBalanceAsync(
        ChatTurn turn, long account, CancellationToken ct = default)
    {
        try
        {
            var report = await _broker.RunAsync(turn.Actor, ReportCatalog.PatientBilling,
                new ReportRequest(AccountNumber: account), "run", turn.CorrelationId, ct);
            return (report, null);
        }
        catch (ReportException ex)
        {
            return (null, ex.Kind switch
            {
                ReportErrorKind.Forbidden => "Your account isn't allowed to look up patient accounts.",
                ReportErrorKind.Unauthenticated => "Please sign in again.",
                ReportErrorKind.Invalid => "That account number isn't valid.",
                _ => "I couldn't reach the billing data just now, so I can't say anything about that account. Please try again shortly.",
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, "I couldn't reach the billing data just now, so I can't say anything about that account. Please try again shortly.");
        }
    }

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
