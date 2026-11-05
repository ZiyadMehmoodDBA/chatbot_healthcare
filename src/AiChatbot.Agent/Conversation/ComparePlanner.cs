using System.Text.RegularExpressions;
using AiChatbot.Agent.Domains;
using AiChatbot.Reporting;

namespace AiChatbot.Agent.Conversation;

// Run one report for two periods and show them side by side. The server runs both; the reply is written from the
// report's own rows (ReportDigests.Compare), so no model sees or states a figure.
public sealed record CompareDecision(
    string ReportId, ReportRequest Current, ReportRequest Prior, string Label, IReadOnlyList<string> Caveats, string Reason)
    : Decision(Reason);

public static partial class ComparePlanner
{
    // Strong signals only. "versus" alone is not enough: "insurance versus patient payments" is a report name.
    [GeneratedRegex(@"(?<![a-z])(compare|compared|comparing|comparison|year[- ]over[- ]year|yoy|month[- ]over[- ]month|up or down|trend|trending|growth|grew|dropped|increased|decreased|changed? (?:from|since))(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex Strong();

    [GeneratedRegex(@"(?<![a-z])(vs\.?|versus|against|than)(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex Weak();

    [GeneratedRegex(@"(?<![a-z])(days to bill|time to bill|billing lag|billing speed|slow to bill|slowest to bill)(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex DaysToBillWords();

    public const string AskPeriod =
        "Which period should I compare? For example: 'payments this month vs last month', 'denials Q1 vs Q2', or " +
        "'claim volume last quarter vs the same quarter last year'.";

    public const string AskReport =
        "Which report should I compare across periods: payments, A/R aging, claim volume, days to bill, or denials?";

    // Words that mean change over time on their own, even with no period named (then we ask which period)
    [GeneratedRegex(@"(?<![a-z])(year[- ]over[- ]year|yoy|month[- ]over[- ]month|up or down|trend|trending|growth|grew|dropped|increased|decreased)(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex ChangeWords();

    // "insurance vs patient payments" is a report name, not a comparison of periods
    [GeneratedRegex(@"insurance\s+(?:and|vs\.?|versus|against|or)\s+patient", RegexOptions.IgnoreCase)]
    private static partial Regex ReportName();

    public static bool IsCompareRequest(string text, DateOnly today)
    {
        var t = ReportName().Replace(text, "insurance-patient");
        if (ChangeWords().IsMatch(t)) return true;
        var periods = Periods.Find(t, today).Count;
        var marker = Periods.HasComparisonMarker(t);
        // compare / changed from need a period (or a "same period last year" marker); vs / versus / against / than need two
        if (Strong().IsMatch(t) && (periods >= 1 || marker)) return true;
        return Weak().IsMatch(t) && (periods >= 2 || marker);
    }

    // null = not a comparison request. A ReplyDecision asks for what is missing. A CompareDecision is ready to run.
    public static Decision? Plan(string text, CoordinatorPlan plan, DateOnly today, int maxTopN)
    {
        if (!IsCompareRequest(text, today)) return null;
        if (plan.Kind == PlanKind.Unsupported) return null; // the plan's own reply says why
        if (plan.Kind != PlanKind.Single)
            return new ReplyDecision(AskReport, TurnKind.Clarification, "compare: report not clear");
        var comparison = Periods.Compare(text, today);
        if (comparison is null)
            return new ReplyDecision(AskPeriod, TurnKind.Clarification, "compare: period missing");

        var lower = text.ToLowerInvariant();
        var reportId = plan.Modules[0].Domain switch
        {
            "payments" => Coordinator.HasWord(lower, "payer") || Coordinator.HasWord(lower, "payers")
                ? ReportCatalog.PaymentsByPayer : ReportCatalog.InsuranceVsPatient,
            "accounts receivable" => ReportCatalog.InsuranceAging,
            "claims" => DaysToBillWords().IsMatch(lower) ? ReportCatalog.DaysToBill : ReportCatalog.ClaimVolume,
            "denials" => ReportCatalog.Denials,
            _ => null,
        };
        if (reportId is null || !ComparisonBuilder.CanCompare(reportId))
            return new ReplyDecision(AskReport, TurnKind.Clarification, "compare: report not comparable");

        // A/R is a balance on a day, not a flow over a period: compare the balance at the end of each period
        ReportRequest Req(DateRange r) => reportId == ReportCatalog.InsuranceAging
            ? new ReportRequest(AsOfDate: r.End, TopN: maxTopN)
            : new ReportRequest(StartDate: r.Start, EndDate: r.End, TopN: maxTopN);

        var caveats = new List<string> { "Periods follow the calendar year; the fiscal calendar is not defined yet." };
        if (comparison.Current.Days != comparison.Prior.Days && reportId != ReportCatalog.InsuranceAging)
            caveats.Add($"The periods have different lengths ({comparison.Current.Days} days and {comparison.Prior.Days} days), so totals are not like for like.");
        if (reportId == ReportCatalog.InsuranceAging)
            caveats.Add("A/R is compared as the balance on the last day of each period; the source keeps no history. See the data-through date.");
        return new CompareDecision(reportId, Req(comparison.Current), Req(comparison.Prior),
            $"{comparison.Current.Label} vs {comparison.Prior.Label}", caveats, $"compare: {reportId}");
    }
}
