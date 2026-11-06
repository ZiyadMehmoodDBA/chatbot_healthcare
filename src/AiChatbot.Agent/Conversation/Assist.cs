using System.Globalization;
using AiChatbot.Reporting;

namespace AiChatbot.Agent.Conversation;

// A suggested reply the user can click. Send is the text posted as their next message, so it goes through the same
// decision system as typed text: a chip can never do more than the user could type.
public sealed record Chip(string Label, string Send);

// What the screen shows next to an answer, built in code from the report evidence: a scope line (who can see what,
// which period, which date basis, how fresh), a note when the result is empty, and chips for the likely next step.
// Never holds figures or free database text.
public sealed record Assist(string? Scope, string? Note, IReadOnlyList<Chip> Chips);

public static class AssistBuilder
{
    public static readonly IReadOnlyList<Chip> PeriodChips =
    [
        new("Last month", "last month"),
        new("This month", "this month"),
        new("Last quarter", "last quarter"),
        new("Year to date", "year to date"),
        new("Last year", "last year"),
    ];

    // Follow-ups that the decision system already understands ("same ..." keeps the period of the last report)
    private static readonly Dictionary<string, Chip[]> Next = new()
    {
        [ReportCatalog.InsuranceVsPatient] = [new("By payer", "same by payer"), new("By practice", "same by practice"), new("By month", "same by month")],
        [ReportCatalog.PaymentsByPayer] = [new("By practice", "same by practice"), new("By month", "same by month"), new("Insurance vs patient", "same insurance versus patient")],
        [GroupedReports.PaymentsByPractice] = [new("By payer", "same by payer"), new("By month", "same by month")],
        [GroupedReports.PaymentsByMonth] = [new("By payer", "same by payer"), new("By practice", "same by practice")],
        [ReportCatalog.Denials] = [new("Denials by payer", "same by payer"), new("By month", "same by month")],
        [GroupedReports.DenialsByPayer] = [new("By month", "same by month"), new("Reasons", "same denial reasons")],
        [ReportCatalog.ClaimVolume] = [new("By practice", "same by practice"), new("By month", "same by month")],
        [GroupedReports.ClaimVolumeByPractice] = [new("By month", "same by month"), new("Days to bill", "same days to bill")],
        [ReportCatalog.DaysToBill] = [new("Claims processed", "same claims processed")],
    };

    private static string Count(Actor actor)
        => actor.Practices.Count == 1 ? "1 practice in your access" : $"{actor.Practices.Count} practices in your access";

    public static string ScopeLine(Actor actor, ReportEvidence e)
        => $"{Count(actor)} · {e.Period} · {e.DateBasis} · {e.ApprovalStatus} · data through {e.DataThrough?.ToString("yyyy-MM-dd") ?? "unknown"}";

    // No rows, or only zeros and blanks: a table with nothing in it
    public static bool IsEmpty(ReportResult r)
        => r.Rows.Count == 0 || r.Rows.All(row => row.All(v => v is null or string or DateOnly or DateTime || IsZero(v)));

    private static bool IsZero(object? v) => v switch
    {
        long l => l == 0,
        int i => i == 0,
        decimal d => d == 0,
        double d => d == 0,
        _ => false,
    };

    private static bool IsBeforeData(ReportEvidence e, out DateOnly start)
    {
        start = default;
        return e.Period.Length >= 10 && DateOnly.TryParseExact(e.Period[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out start)
            && e.DataThrough is { } through && start > through;
    }

    // The code-written reply for a question whose every table came back empty: the model is not asked to describe zeros
    public static string EmptyReply(IReadOnlyList<ReportResult> reports)
    {
        var e = reports[0].Evidence;
        // Known gap, checked 2026-11-05: the days-to-bill source table has no rows in this database, for any period
        if (reports.All(r => r.Evidence.ReportId == ReportCatalog.DaysToBill))
            return "Days to bill reads a source table that had no rows when I last checked (2026-11-05), so no period returns " +
                   "figures until it is loaded. I can show claim submissions for the same period instead.";
        var periods = string.Join(" and ", reports.Select(r => r.Evidence.Period).Distinct());
        var through = e.DataThrough is { } d ? $" The data runs through {d:yyyy-MM-dd}." : "";
        return IsBeforeData(e, out _)
            ? $"I don't have data for {periods} yet.{through} Pick a period inside that and I'll run it."
            : $"I looked at {periods} and found no records.{through} That can mean a quiet period or a source that isn't loaded yet. Try another period, or tell me which practice you expected.";
    }

    // Words a model uses to back away from figures it was given. The prompt forbids them; this is the check.
    private static readonly System.Text.RegularExpressions.Regex HedgeWords = new(
        "placeholder|(can't|cannot|can not|unable to|not able to|couldn't) (provide|give|share|state|see|show)|at this moment|at the moment|will be filled in",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    public static bool IsHedge(string text) => HedgeWords.IsMatch(text);

    // The answer when the model hedged: each report's own one-sentence summary, in plain words, written from its rows
    public static string DigestReply(IReadOnlyList<ReportResult> reports)
    {
        var lines = reports.Take(3).Select(r => ReportDigests.From(r, r.Evidence.ReportId).Headline);
        return "Here's what the data shows. " + string.Join(" ", lines);
    }

    // null = nothing to add (no report, or a patient lookup, which never gets suggestions)
    public static Assist? ForReports(Actor actor, IReadOnlyList<ReportResult> reports)
    {
        if (reports.Count == 0) return null;
        var last = reports[^1];
        if (last.Evidence.ReportId == ReportCatalog.PatientBilling) return null;
        var scope = ScopeLine(actor, last.Evidence);

        if (reports.All(IsEmpty))
        {
            // Offer the newest full month the data covers instead of a period that cannot work
            var chips = new List<Chip>();
            if (last.Evidence.DataThrough is { } through)
            {
                var latest = new DateOnly(through.Year, through.Month, 1).AddMonths(-1);
                var name = latest.ToString("MMMM", CultureInfo.InvariantCulture);
                chips.Add(new Chip($"{name} (latest full month)", $"same for {name}"));
            }
            if (last.Evidence.ReportId == ReportCatalog.DaysToBill)
                chips.Insert(0, new Chip("Claim submissions", "same submissions"));
            chips.Add(new Chip("Year to date", "same for year to date"));
            return new Assist(scope, EmptyReply(reports), chips);
        }

        var follow = Next.TryGetValue(last.Evidence.ReportId, out var list) ? list : [];
        return new Assist(scope, null, follow);
    }
}
