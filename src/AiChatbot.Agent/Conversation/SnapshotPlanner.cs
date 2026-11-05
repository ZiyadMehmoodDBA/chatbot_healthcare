using System.Text.RegularExpressions;
using AiChatbot.Reporting;

namespace AiChatbot.Agent.Conversation;

// "How are we doing?": one period against the one before, across the main aggregate reports. The server runs every
// comparison and writes the lines from the rows; no model sees or states a figure.
public sealed record SnapshotDecision(IReadOnlyList<CompareDecision> Parts, string Label, string Reason) : Decision(Reason);

public static partial class SnapshotPlanner
{
    [GeneratedRegex(@"\bhow\s+(?:are|is)\s+(?:we|the\s+(?:business|practice|practices|company|revenue\s+cycle|rcm|billing|team)|business|revenue|collections)\s+doing\b|\bhow\s?s\s+(?:the\s+)?(?:business|revenue|billing|rcm)\b|\bexecutive\s+(?:summary|snapshot|overview|dashboard)\b|\bbusiness\s+(?:summary|snapshot|overview|health|performance)\b|\b(?:key\s+metrics|kpis?|scorecard|state\s+of\s+the\s+business|big\s+picture)\b|\bgive\s+me\s+(?:an?\s+)?(?:overview|snapshot)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SnapshotRx();

    // The reports in the snapshot, in reading order. Only those the caller's role may run are used.
    public static readonly string[] ReportIds =
    [
        ReportCatalog.InsuranceVsPatient, ReportCatalog.ClaimVolume, ReportCatalog.Denials,
        ReportCatalog.InsuranceAging, ReportCatalog.DaysToBill,
    ];

    public const string NothingAllowed = "Your role cannot run any of the reports in the business snapshot.";

    public static bool IsSnapshotRequest(string text) => SnapshotRx().IsMatch(text.Replace('’', ' ').Replace('\'', ' '));

    public static Decision? Plan(string text, Actor actor, DateOnly today, int maxTopN)
    {
        if (!IsSnapshotRequest(text)) return null;
        var ids = ReportIds
            .Where(id => ComparisonBuilder.CanCompare(id) && ReportCatalog.Find(id)!.AllowedRoles.Contains(actor.Role)).ToList();
        if (ids.Count == 0) return new ReplyDecision(NothingAllowed, TurnKind.Refused, "snapshot: role has no reports");

        // A named period or "X vs Y" is honoured; otherwise last full month against the month before
        var pair = Periods.Compare(text, today);
        if (pair is null)
        {
            var last = Periods.Resolve("last month", today)!;
            pair = new Periods.Comparison(last, Periods.Previous(last));
        }

        ReportRequest Req(string id, DateRange r) => id == ReportCatalog.InsuranceAging
            ? new ReportRequest(AsOfDate: r.End, TopN: maxTopN)
            : new ReportRequest(StartDate: r.Start, EndDate: r.End, TopN: maxTopN);

        var parts = ids.Select(id =>
        {
            var caveats = new List<string> { "Periods follow the calendar year; the fiscal calendar is not defined yet." };
            if (id == ReportCatalog.InsuranceAging)
                caveats.Add("A/R is compared as the balance on the last day of each period; the source keeps no history.");
            else if (pair.Current.Days != pair.Prior.Days)
                caveats.Add($"The periods have different lengths ({pair.Current.Days} days and {pair.Prior.Days} days), so totals are not like for like.");
            return new CompareDecision(id, Req(id, pair.Current), Req(id, pair.Prior), $"{pair.Current.Label} vs {pair.Prior.Label}",
                caveats, $"snapshot: {id}");
        }).ToList();
        return new SnapshotDecision(parts, $"{pair.Current.Label} vs {pair.Prior.Label}", "snapshot");
    }
}
