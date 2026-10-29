using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Reporting;

public static class CsvExport
{
    // Text from the database (payer names) can start with = + - @ and run as a formula when the file is opened in
    // Excel, so such cells are prefixed with an apostrophe. Numbers are written as numbers, not as text.
    public static string ToCsv(ReportResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", result.Columns.Select(c => Cell(c.Label, true))));
        foreach (var row in result.Rows)
            sb.AppendLine(string.Join(",", row.Select((v, i) => Cell(v, result.Columns[i].Type == ColumnType.Text))));
        sb.AppendLine();
        var e = result.Evidence;
        sb.AppendLine(Cell($"Report: {e.ReportId} v{e.MetricVersion} ({e.ApprovalStatus})", true));
        sb.AppendLine(Cell($"Period: {e.Period}; date basis: {e.DateBasis}", true));
        sb.AppendLine(Cell($"Generated (UTC): {e.GeneratedUtc:yyyy-MM-dd HH:mm}; rows: {e.RowCount}{(e.Truncated ? " (truncated)" : "")}", true));
        return sb.ToString();
    }

    private static string Cell(object? v, bool text)
    {
        var s = v switch
        {
            null => "",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
        if (text && s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}

// Audit lines carry actor, report, filters, outcome and counts: no result rows and no patient values.
public sealed class LoggerReportAudit(ILogger<LoggerReportAudit> log) : IReportAudit
{
    public void Record(ReportAuditEntry e)
        => log.LogInformation(
            "ReportAudit {Action} {ReportId} user={UserId} role={Role} outcome={Outcome} rows={Rows} ms={Ms} corr={CorrelationId} filters={Filters}",
            e.Action, e.ReportId, e.UserId, e.Role, e.Outcome, e.RowCount, e.DurationMs, e.CorrelationId, string.Join(";", e.Filters.Select(f => $"{f.Key}={f.Value}")));
}
