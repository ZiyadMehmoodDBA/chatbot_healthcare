using System.Globalization;
using System.Text.Json;
using AiChatbot.Safety;
using Microsoft.Extensions.AI;

namespace AiChatbot.Agent.Tools;

// Values are keyed by the {PLACEHOLDER} name the model must write; the server fills them in on the way out.
public sealed record ToolResult(DataClass Class, IReadOnlyDictionary<string, string> Values, string? Error = null)
{
    public static ToolResult Fail(string error) => new(DataClass.Public, new Dictionary<string, string>(), error);
}

// Tool declarations plus execution. The model supplies only tokens/allow-listed names; scope is checked here.
public sealed class ReportTools(IReportDataReader reader, ITokenizationService tokens)
{
    public const string PatientSummaryName = "GetPatientSummary";
    public const string FacilityMetricsName = "GetFacilityMetrics";

    public IReadOnlyList<AITool> Declarations { get; } =
    [
        AIFunctionFactory.CreateDeclaration(
            PatientSummaryName,
            "Get admissions, last visit and open incidents for one patient account. Pass the account token (such as {ACCT_1}) exactly as it appears in the conversation.",
            JsonDocument.Parse("""{"type":"object","properties":{"account":{"type":"string","description":"Account token, for example {ACCT_1}"}},"required":["account"],"additionalProperties":false}""").RootElement),
        AIFunctionFactory.CreateDeclaration(
            FacilityMetricsName,
            "Get readmission rate, average length of stay and incident count for one facility.",
            JsonDocument.Parse("""{"type":"object","properties":{"facility":{"type":"string","enum":["north","south","east","west"]}},"required":["facility"],"additionalProperties":false}""").RootElement),
    ];

    public ToolResult Execute(Guid conversationId, string name, IDictionary<string, object?>? args)
    {
        switch (name)
        {
            case PatientSummaryName:
            {
                // Unknown token, unknown number and missing data all look the same to the model
                if (Arg(args, "account") is not { } raw || !tokens.TryResolveAccount(conversationId, raw, out var account)
                    || reader.GetPatientSummary(account) is not { } p)
                    return ToolResult.Fail("not_found");
                return new ToolResult(DataClass.PatientLevel, new Dictionary<string, string>
                {
                    ["PATIENT_ADMISSIONS"] = p.Admissions.ToString(CultureInfo.InvariantCulture),
                    ["PATIENT_LAST_VISIT"] = p.LastVisit,
                    ["PATIENT_OPEN_INCIDENTS"] = p.OpenIncidents.ToString(CultureInfo.InvariantCulture),
                });
            }
            case FacilityMetricsName:
            {
                if (Arg(args, "facility") is not { } f || reader.GetFacilityMetrics(f) is not { } m)
                    return ToolResult.Fail("not_found");
                return new ToolResult(DataClass.Aggregate, new Dictionary<string, string>
                {
                    ["FACILITY_READMISSION_RATE"] = m.ReadmissionRatePct.ToString("0.0", CultureInfo.InvariantCulture) + "%",
                    ["FACILITY_AVG_LENGTH_OF_STAY"] = m.AvgLengthOfStayDays.ToString("0.0", CultureInfo.InvariantCulture) + " days",
                    ["FACILITY_INCIDENT_COUNT"] = m.IncidentCount.ToString(CultureInfo.InvariantCulture),
                    ["FACILITY_DATA_AS_OF"] = m.AsOf,
                });
            }
            default:
                return ToolResult.Fail("unknown_tool");
        }
    }

    private static string? Arg(IDictionary<string, object?>? args, string key)
        => args is not null && args.TryGetValue(key, out var v) ? v?.ToString() : null;
}
