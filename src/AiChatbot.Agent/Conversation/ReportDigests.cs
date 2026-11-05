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
                return $"{p}: {Num(rows[0][2])} claims with a denial or rejection status entry " +
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

    private static string Count(int n, string noun) => $"{n.ToString("N0", CultureInfo.InvariantCulture)} {noun}{(n == 1 ? "" : "s")}";
    private static string Money(object? o) => Dec(o).ToString("N2", CultureInfo.InvariantCulture);
    private static string Num(object? o) => Long(o).ToString("N0", CultureInfo.InvariantCulture);
    private static string Days(object? o) => Dec(o).ToString("0.0", CultureInfo.InvariantCulture);
    private static string Pct(object? o) => o is null ? "n/a" : Dec(o).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    private static string Text(object? o) => Convert.ToString(o, CultureInfo.InvariantCulture) ?? "";
    private static decimal Dec(object? o) => o is null ? 0m : Convert.ToDecimal(o, CultureInfo.InvariantCulture);
    private static long Long(object? o) => o is null ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture);
}
