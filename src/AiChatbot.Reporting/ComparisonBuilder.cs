using System.Globalization;

namespace AiChatbot.Reporting;

// Puts two runs of the same report side by side: the grouping columns (text), then for each measure the current value,
// the prior value, the change and the change in percent. All arithmetic is decimal and done here, never by a model.
public static class ComparisonBuilder
{
    // Which measures are compared per report. Wide reports (aging has eight money columns) would not fit as a table.
    private static readonly Dictionary<string, string[]> Measures = new()
    {
        [ReportCatalog.InsuranceVsPatient] = ["claimCount", "insurancePayments", "patientPayments", "combinedPayments"],
        [ReportCatalog.PaymentsByPayer] = ["claimCount", "combinedPayments"],
        [ReportCatalog.InsuranceAging] = ["claimCount", "age120plus", "totalBalance"],
        [ReportCatalog.ClaimVolume] = ["claimCount", "lineCount", "charges"],
        [ReportCatalog.DaysToBill] = ["claimCount", "avgDays"],
        [ReportCatalog.Denials] = ["claimCount"],
    };

    public static bool CanCompare(string reportId) => Measures.ContainsKey(reportId) || GroupedReports.Find(reportId) is not null;

    // Grouped reports compare their money and count measures, but not every column of a wide one
    private static string[]? MeasuresFor(string reportId)
        => Measures.TryGetValue(reportId, out var m) ? m
         : GroupedReports.Find(reportId) is { } g ? g.Measures.Select(x => x.Name).Where(n => n != "recordCount" && n != "lineCount" && n != "insurancePayments" && n != "patientPayments" && n != "amountWithheld").ToArray()
         : null;

    public static ReportResult Build(
        ReportResult current, ReportResult prior, IReadOnlyList<string>? extraCaveats = null, string? incomplete = null)
    {
        var id = current.Evidence.ReportId;
        if (MeasuresFor(id) is not { } wanted)
            throw new ReportException(ReportErrorKind.Invalid, "This report cannot be compared across periods");
        var cols = current.Columns;
        var keys = Enumerable.Range(0, cols.Count).Where(i => cols[i].Type == ColumnType.Text).ToArray();
        var measures = wanted.Select(w => Array.FindIndex(cols.ToArray(), c => c.Name == w)).Where(i => i >= 0).ToArray();

        var outCols = new List<ReportColumn>();
        outCols.AddRange(keys.Select(i => cols[i]));
        foreach (var m in measures)
        {
            var c = cols[m];
            outCols.Add(c with { Name = c.Name, Label = $"{c.Label} (current)" });
            outCols.Add(c with { Name = c.Name + "Prior", Label = $"{c.Label} (prior)" });
            outCols.Add(c with { Name = c.Name + "Change", Label = "Change" });
            if (c.Type != ColumnType.Percent)
                outCols.Add(new ReportColumn(c.Name + "ChangePct", "Change %", ColumnType.Percent));
        }

        string Key(IReadOnlyList<object?> row) => string.Join("\u001f", keys.Select(i => Convert.ToString(row[i], CultureInfo.InvariantCulture)));
        var priorByKey = prior.Rows.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
        var seen = new HashSet<string>();
        var rows = new List<IReadOnlyList<object?>>();

        IReadOnlyList<object?> Merge(IReadOnlyList<object?>? cur, IReadOnlyList<object?>? pri)
        {
            var src = cur ?? pri!;
            var o = new List<object?>();
            o.AddRange(keys.Select(i => src[i]));
            foreach (var m in measures)
            {
                var a = cur is null ? (decimal?)null : Dec(cur[m]);
                var b = pri is null ? (decimal?)null : Dec(pri[m]);
                var change = (a ?? 0m) - (b ?? 0m);
                o.Add(a);
                o.Add(b);
                o.Add(change);
                if (cols[m].Type != ColumnType.Percent)
                    o.Add(b is null or 0m ? null : Math.Round(change / b.Value * 100m, 2, MidpointRounding.AwayFromZero));
            }
            return o;
        }

        foreach (var row in current.Rows)
        {
            var k = Key(row);
            seen.Add(k);
            rows.Add(Merge(row, priorByKey.GetValueOrDefault(k)));
        }
        foreach (var row in prior.Rows)
            if (!seen.Contains(Key(row))) rows.Add(Merge(null, row));

        var ce = current.Evidence;
        var pe = prior.Evidence;
        var caveats = ce.Caveats.Concat(pe.Caveats).Distinct().ToList();
        caveats.Add("Comparison: change is current minus prior. Change % is blank when the prior value is zero or missing. " +
                    "A row present in only one period shows blank for the other.");
        if (extraCaveats is not null) caveats.AddRange(extraCaveats);
        var filters = new Dictionary<string, string>(ce.Filters) { ["comparison"] = "true", ["priorPeriod"] = pe.Period };
        if (incomplete is not null)
        {
            filters["incomplete"] = incomplete;
            caveats.Add($"The {incomplete} period runs past the data-through date, so days after it are not loaded yet and the comparison is incomplete.");
        }
        var through = new[] { ce.DataThrough, pe.DataThrough }.Where(d => d is not null).Max();
        var evidence = ce with
        {
            Period = $"{ce.Period} vs {pe.Period}",
            Filters = filters,
            RowCount = rows.Count,
            Truncated = ce.Truncated || pe.Truncated,
            Caveats = caveats,
            DataThrough = through,
            ApprovalStatus = ce.ApprovalStatus == "Approved" && pe.ApprovalStatus == "Approved" ? "Approved" : "Draft",
        };
        return new ReportResult(outCols, rows, evidence);
    }

    private static decimal Dec(object? o) => o is null ? 0m : Convert.ToDecimal(o, CultureInfo.InvariantCulture);
}
