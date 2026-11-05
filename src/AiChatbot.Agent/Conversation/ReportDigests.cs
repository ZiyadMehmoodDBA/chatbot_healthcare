using System.Globalization;
using AiChatbot.Reporting;

namespace AiChatbot.Agent.Conversation;

// One-line, server-written summaries of a report result. Used for session memory ("summarize that", recap) and for the
// patient lookup reply. Every figure is read from the report service's own rows; no model text is involved.
public static class ReportDigests
{
    public static ReportDigest From(ReportResult r, string name)
    {
        var e = r.Evidence;
        var through = e.DataThrough is { } d ? $" Data through {d:yyyy-MM-dd}." : "";
        return new ReportDigest(e.ReportId, name, e.Period, r.Rows.Count, Headline(r) + through);
    }

    public static string Headline(ReportResult r)
    {
        var rows = r.Rows;
        if (rows.Count == 0) return "No matching records.";
        var id = r.Evidence.ReportId;
        var p = r.Evidence.Period;
        switch (id)
        {
            case ReportCatalog.InsuranceVsPatient:
            {
                var x = rows[0];
                if (Long(x[1]) == 0) return "No matching records.";
                return $"{p}: insurance {Money(x[2])} ({Pct(x[6])}) versus patient {Money(x[3])} ({Pct(x[7])}), " +
                       $"combined {Money(x[4])} over {Num(x[0])} claims.";
            }
            case ReportCatalog.PaymentsByPayer:
                return $"{p}: {Count(rows.Count, "payer")} listed; top is {Text(rows[0][1])} at {Money(rows[0][5])}; " +
                       $"listed total {Money(rows.Sum(x => Dec(x[5])))}.";
            case ReportCatalog.InsuranceAging:
                return $"{p}: {Count(rows.Count, "payer")} with open balances; largest is {Text(rows[0][1])} at {Money(rows[0][9])}; " +
                       $"listed total {Money(rows.Sum(x => Dec(x[9])))}.";
            case ReportCatalog.ClaimVolume:
                return $"{p}: {Count(rows.Count, "provider row")}; {Num(rows.Sum(x => Long(x[3])))} claims and " +
                       $"{Money(rows.Sum(x => Dec(x[5])))} charges listed; busiest is {Text(rows[0][2])} with {Num(rows[0][3])} claims.";
            case ReportCatalog.DaysToBill:
                return $"{p}: overall average {Days(rows[0][3])} days from service to first submission " +
                       $"across {Num(rows[0][2])} claims (longest {Num(rows[0][4])} days).";
            case ReportCatalog.Denials:
                return $"{p}: {Num(rows[0][2])} claims with a claim-status entry (accepted or rejected, not all denials) " +
                       $"({Num(rows[0][3])} status records) in {Count(rows.Count - 1, "status and reason group")}. Counts only, no dollar amounts.";
            case ReportCatalog.PatientBilling:
            {
                var t = rows[^1];
                return $"{Text(t[1])}, {Num(t[2])} lines: charges {Money(t[7])}, insurance paid {Money(t[8])}, patient paid {Money(t[9])}, " +
                       $"adjustments {Money(t[10])}, patient responsibility {Money(t[11])}, patient balance {Money(t[12])}.";
            }
            default:
                return $"{p}: {Count(rows.Count, "row")}.";
        }
    }

    // One sentence for a comparison table (ComparisonBuilder output), from its own rows: the headline measure for the
    // current period, the prior period, and the change. Overall rows are used where the report has them.
    public static string Compare(ReportResult r)
    {
        var id = r.Evidence.ReportId;
        var (measure, label, kind, overall) = id switch
        {
            ReportCatalog.InsuranceVsPatient => ("combinedPayments", "Combined payments", 'm', true),
            ReportCatalog.PaymentsByPayer => ("combinedPayments", "Combined payments across the listed payers", 'm', false),
            ReportCatalog.InsuranceAging => ("totalBalance", "Open insurance A/R across the listed payers", 'm', false),
            ReportCatalog.ClaimVolume => ("claimCount", "Unique claims across the listed providers", 'n', false),
            ReportCatalog.DaysToBill => ("avgDays", "Average days to bill", 'd', true),
            ReportCatalog.Denials => ("claimCount", "Claims with a status entry (accepted or rejected)", 'n', true),
            _ => ("", "", 'n', true),
        };
        var names = r.Columns.Select(c => c.Name).ToList();
        var ci = names.IndexOf(measure);
        if (ci < 0 || r.Rows.Count == 0) return $"{r.Evidence.Period}: no matching records in either period.";
        decimal cur, pri;
        if (overall) { cur = Dec(r.Rows[0][ci]); pri = Dec(r.Rows[0][ci + 1]); }
        else { cur = r.Rows.Sum(x => Dec(x[ci])); pri = r.Rows.Sum(x => Dec(x[ci + 1])); }
        string F(decimal v) => kind switch
        {
            'm' => v.ToString("N2", CultureInfo.InvariantCulture),
            'd' => v.ToString("0.0", CultureInfo.InvariantCulture),
            _ => v.ToString("N0", CultureInfo.InvariantCulture),
        };
        var change = cur - pri;
        var dir = change > 0 ? "up" : change < 0 ? "down" : "unchanged";
        var pct = pri != 0
            ? $" ({(change >= 0 ? "+" : "-")}{Math.Abs(Math.Round(change / pri * 100m, 1, MidpointRounding.AwayFromZero)).ToString("0.0", CultureInfo.InvariantCulture)}%)"
            : "";
        var move = change == 0 ? "unchanged" : $"{dir} {F(Math.Abs(change))}{pct}";
        var through = r.Evidence.DataThrough is { } d ? $" Data through {d:yyyy-MM-dd}." : "";
        var partial = r.Evidence.Filters.TryGetValue("incomplete", out var which)
            ? $" Note: the {which} period runs past the data-through date, so this comparison is incomplete."
            : "";
        return $"{label}, {r.Evidence.Period}: {F(cur)} versus {F(pri)}, {move}.{through}{partial}";
    }

    private static string Count(int n, string noun) => $"{n.ToString("N0", CultureInfo.InvariantCulture)} {noun}{(n == 1 ? "" : "s")}";
    private static string Money(object? o) => Dec(o).ToString("N2", CultureInfo.InvariantCulture);
    private static string Num(object? o) => Long(o).ToString("N0", CultureInfo.InvariantCulture);
    private static string Days(object? o) => Dec(o).ToString("0.0", CultureInfo.InvariantCulture);
    private static string Pct(object? o) => o is null ? "n/a" : Dec(o).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    private static string Text(object? o) => Convert.ToString(o, CultureInfo.InvariantCulture) ?? "";
    private static decimal Dec(object? o) => o is null ? 0m : Convert.ToDecimal(o, CultureInfo.InvariantCulture);
    private static long Long(object? o) => o is null ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture);
}
