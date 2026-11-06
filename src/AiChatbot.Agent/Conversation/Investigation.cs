using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AiChatbot.Reporting;

namespace AiChatbot.Agent.Conversation;

// "Why did collections decrease?" The server picks the approved reports a playbook calls for, runs them through the
// broker for this period and the one before, calculates the changes and who contributed, and writes the answer. No model
// sees or states a figure. Every statement is a calculation from the rows or is labelled as an inference or an unknown.
public sealed record InvestigationDecision(
    string PlaybookId, DateRange Current, DateRange Prior, bool DefaultedPeriod, string? PayerWords, string Reason) : Decision(Reason);

public static partial class InvestigationPlanner
{
    [GeneratedRegex(@"\b(?:why|what\s+(?:caused|is\s+causing|happened\s+(?:to|with)|is\s+behind|explains?)|how\s+come|reason\s+(?:for|why)|explain)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WhyRx();

    [GeneratedRegex(@"\b(?:collections?|collected|revenue|payments?|paid|cash|reimbursements?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MoneyRx();

    [GeneratedRegex(@"\b(?:decreas\w*|drop\w*|fell|fall\w*|down|declin\w*|lower|less|lost|shrunk|shrink\w*|increas\w*|rose|rising|up|higher|grew|grow\w*|jump\w*|spik\w*|chang\w*|different|worse|better)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MoveRx();

    [GeneratedRegex(@"\b(?:a/r|ar|receivables?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ArRx();

    [GeneratedRegex(@"\b(?:denials?|denied|rejections?|rejected)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DenialRx();

    // "why did medicare stop paying", "why has aetna stopped paying us"
    [GeneratedRegex(@"\bwhy\s+(?:did|has|have|is|are|does|do)\s+(?:the\s+)?(?:payer\s+)?(?<who>[a-z0-9&' -]{2,40}?)\s+(?:stop|stopped|stops|not\s+paying|no\s+longer\s+pay\w*)", RegexOptions.IgnoreCase)]
    private static partial Regex StoppedRx();

    public static Decision? Plan(string text, Actor actor, DateOnly today)
    {
        if (!WhyRx().IsMatch(text)) return null;
        string? playbook = null;
        string? payer = null;
        if (StoppedRx().Match(text) is { Success: true } stop)
        {
            playbook = "payer-stopped";
            payer = stop.Groups["who"].Value.Trim();
        }
        else if (ArRx().IsMatch(text) && MoveRx().IsMatch(text)) playbook = "ar-change";
        else if (DenialRx().IsMatch(text) && MoveRx().IsMatch(text)) playbook = "denials-change";
        else if (MoneyRx().IsMatch(text) && MoveRx().IsMatch(text)) playbook = "collections-change";
        if (playbook is null) return null;

        // A/R has no history to compare: that playbook never needs a period
        var pair = Periods.Compare(text, today);
        var defaulted = false;
        if (pair is null)
        {
            var last = Periods.Resolve("last month", today)!;
            pair = new Periods.Comparison(last, Periods.Previous(last));
            defaulted = true;
        }
        return new InvestigationDecision(playbook, pair.Current, pair.Prior, defaulted, payer, $"investigate: {playbook}");
    }

    // The latest whole month the data covers, and the month before it
    public static Periods.Comparison LatestFullMonths(DateOnly dataThrough)
    {
        var lastDay = new DateOnly(dataThrough.Year, dataThrough.Month, DateTime.DaysInMonth(dataThrough.Year, dataThrough.Month));
        var first = dataThrough == lastDay ? new DateOnly(dataThrough.Year, dataThrough.Month, 1)
                                           : new DateOnly(dataThrough.Year, dataThrough.Month, 1).AddMonths(-1);
        var cur = new DateRange(first, first.AddMonths(1).AddDays(-1), "the latest full month");
        return new Periods.Comparison(cur, Periods.Previous(cur));
    }
}

// Pure calculations over comparison tables (ComparisonBuilder output). Nothing here reads a database or a model.
public static class FinancialAnalysis
{
    public sealed record Mover(string Name, decimal Current, decimal Prior, decimal Change);

    private static decimal Dec(object? o) => o is null ? 0m : Convert.ToDecimal(o, CultureInfo.InvariantCulture);

    public static int Col(ReportResult r, string name) => r.Columns.ToList().FindIndex(c => c.Name == name);

    // Overall row of a comparison with no grouping (or the first row when the report is grouped)
    public static (decimal Cur, decimal Prior)? Overall(ReportResult? r, string measure)
    {
        if (r is null || r.Rows.Count == 0) return null;
        var i = Col(r, measure);
        var j = Col(r, measure + "Prior");
        return i < 0 || j < 0 ? null : (Dec(r.Rows[0][i]), Dec(r.Rows[0][j]));
    }

    public static (decimal Cur, decimal Prior)? Sum(ReportResult? r, string measure)
    {
        if (r is null) return null;
        var i = Col(r, measure);
        var j = Col(r, measure + "Prior");
        return i < 0 || j < 0 ? null : (r.Rows.Sum(x => Dec(x[i])), r.Rows.Sum(x => Dec(x[j])));
    }

    // Rows of a grouped comparison as movers, named by the text columns joined
    public static List<Mover> Movers(ReportResult? r, string measure)
    {
        var res = new List<Mover>();
        if (r is null) return res;
        var i = Col(r, measure);
        var j = Col(r, measure + "Prior");
        if (i < 0 || j < 0) return res;
        var keys = Enumerable.Range(0, r.Columns.Count).Where(k => r.Columns[k].Type == ColumnType.Text).ToList();
        foreach (var row in r.Rows)
        {
            var name = string.Join(" ", keys.Select(k => Convert.ToString(row[k], CultureInfo.InvariantCulture)).Where(s => !string.IsNullOrWhiteSpace(s)));
            var cur = Dec(row[i]);
            var pri = Dec(row[j]);
            res.Add(new Mover(name, cur, pri, cur - pri));
        }
        return res;
    }

    public static decimal? Pct(decimal change, decimal prior) => prior == 0 ? null : Math.Round(change / prior * 100m, 1, MidpointRounding.AwayFromZero);

    // Share of the net change that the biggest movers in the direction of the change account for
    public static (List<Mover> Top, decimal SharePct) Concentration(IReadOnlyList<Mover> movers, decimal totalChange, int take)
    {
        var sameWay = movers.Where(m => Math.Sign(m.Change) == Math.Sign(totalChange) && m.Change != 0)
            .OrderByDescending(m => Math.Abs(m.Change)).Take(take).ToList();
        var share = totalChange == 0 ? 0m : Math.Round(sameWay.Sum(m => m.Change) / totalChange * 100m, 0, MidpointRounding.AwayFromZero);
        return (sameWay, share);
    }

    public static string N0(decimal v) => v.ToString("N0", CultureInfo.InvariantCulture);
    public static string Signed(decimal v) => (v >= 0 ? "+" : "-") + Math.Abs(v).ToString("N0", CultureInfo.InvariantCulture);
    public static string Word(decimal change) => change > 0 ? "up" : change < 0 ? "down" : "unchanged";
}
