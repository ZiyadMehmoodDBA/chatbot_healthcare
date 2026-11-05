using System.Globalization;
using System.Text.RegularExpressions;
using AiChatbot.Reporting;

namespace AiChatbot.Agent.Conversation;

// Where a table row can lead: the month-by-month view of one payer, practice or provider. The keys are identifiers
// (payer, practice and provider codes, not patient data) remembered by the server for the last table shown.
// The user points at a row by number ("drill into row 3"); no identifier is ever typed or taken from model text.
public sealed record DrillTarget(string Kind, IReadOnlyList<string> Keys, DateOnly Start, DateOnly End, string TargetReportId);

public sealed record DrillDecision(string ReportId, ReportRequest Request, string Label, string Reason) : Decision(Reason);

public static partial class DrillPlanner
{
    // report -> (key column, kind, month report). The kind decides which filter the key becomes.
    private static readonly Dictionary<string, (string Column, string Kind, string Target)> Map = new()
    {
        [ReportCatalog.PaymentsByPayer] = ("payerId", "payer", GroupedReports.PaymentsByMonth),
        [GroupedReports.PaymentsByPractice] = ("practiceCode", "practice", GroupedReports.PaymentsByMonth),
        [ReportCatalog.ClaimVolume] = ("providerId", "provider", GroupedReports.ClaimVolumeByMonth),
        [GroupedReports.ClaimVolumeByPractice] = ("practiceCode", "practice", GroupedReports.ClaimVolumeByMonth),
        [GroupedReports.FinancialsByPractice] = ("practiceCode", "practice", GroupedReports.FinancialsByMonth),
        [GroupedReports.DenialsByPayer] = ("payerId", "payer", GroupedReports.DenialsByMonth),
        [GroupedReports.ResubmissionsByPayer] = ("payerId", "payer", GroupedReports.ResubmissionsByMonth),
    };

    public static bool CanDrill(string reportId) => Map.ContainsKey(reportId);

    // Built from a finished report; null when the report has no drill path or covers no period of days.
    public static DrillTarget? For(ReportResult r)
    {
        if (!Map.TryGetValue(r.Evidence.ReportId, out var m) || r.Evidence.Filters.ContainsKey("comparison")) return null;
        var f = r.Evidence.Filters;
        if (!f.TryGetValue("startDate", out var s) || !f.TryGetValue("endDate", out var e)
            || !DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateOnly.TryParseExact(e, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
            return null;
        var col = r.Columns.ToList().FindIndex(c => c.Name == m.Column);
        if (col < 0) return null;
        // Row positions are kept so "row 3" means the third row shown; rows with no key (overall rows) hold ""
        var keys = r.Rows.Select(row => Convert.ToString(row[col], CultureInfo.InvariantCulture) ?? "")
            .Select(k => k.All(char.IsDigit) ? k : "").ToList();
        return keys.Any(k => k.Length > 0) ? new DrillTarget(m.Kind, keys, start, end, m.Target) : null;
    }

    [GeneratedRegex(@"\b(?:drill(?:\s+down)?|break\s*down|zoom)\s*(?:in(?:to)?|on)?\s*(?:the\s+)?(?:row|line|number|no\.?|#)\s*#?\s*(\d{1,3})\b|\bmonthly\s+(?:trend|breakdown)\s+(?:for|of)\s+(?:the\s+)?row\s*#?\s*(\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex DrillRx();

    public const string NothingToDrill =
        "I can drill into a table by payer, practice or provider (payments by payer, claim volume, charges by practice, " +
        "denials or resubmissions by payer). Run one of those first, then say 'drill into row 3'.";

    public static bool IsDrillRequest(string text) => DrillRx().IsMatch(text);

    public static Decision? Plan(string text, SessionSnapshot session, int maxTopN)
    {
        var m = DrillRx().Match(text);
        if (!m.Success) return null;
        var row = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value, CultureInfo.InvariantCulture);
        if (session.LastDrill is not { } d)
            return new ReplyDecision(NothingToDrill, TurnKind.Clarification, "drill: no drillable table");
        if (row < 1 || row > d.Keys.Count)
            return new ReplyDecision($"The last table has {d.Keys.Count} rows. Which row number should I drill into?", TurnKind.Clarification, "drill: row out of range");
        var key = d.Keys[row - 1];
        if (key.Length == 0 || !long.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            return new ReplyDecision($"Row {row} is a total, not one {d.Kind}. Pick a row for a single {d.Kind}.", TurnKind.Clarification, "drill: row has no key");

        var req = new ReportRequest(StartDate: d.Start, EndDate: d.End, TopN: maxTopN,
            PayerId: d.Kind == "payer" ? id : null,
            ProviderId: d.Kind == "provider" ? id : null,
            PracticeCodes: d.Kind == "practice" ? [id] : null);
        return new DrillDecision(d.TargetReportId, req, $"{d.Kind} in row {row}, month by month", $"drill: {d.Kind} row {row}");
    }
}
