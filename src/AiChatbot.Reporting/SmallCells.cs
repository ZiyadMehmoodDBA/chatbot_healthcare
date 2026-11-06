namespace AiChatbot.Reporting;

// Small groups can point at individuals. Rows of a grouped report whose claim count is below the threshold keep their
// labels and ids but lose their counts and amounts. Patient-level lookups are not grouped and are not touched.
public static class SmallCells
{
    private static readonly string[] CountColumns = ["claimCount", "recordCount", "submissionCount", "lineCount"];

    // Returns the number of rows hidden, or null when nothing was
    public static int? Suppress(ReportDefinition def, IList<IReadOnlyList<object?>> rows, int min)
    {
        if (min <= 1 || def.Id == ReportCatalog.PatientBilling || !def.Columns.Any(c => c.Type == ColumnType.Text)) return null;
        var counts = def.Columns.Select((c, i) => (c, i)).Where(x => CountColumns.Contains(x.c.Name)).Select(x => x.i).ToArray();
        if (counts.Length == 0) return null;
        var figures = def.Columns.Select((c, i) => (c, i))
            .Where(x => x.c.Type is ColumnType.Integer or ColumnType.Money or ColumnType.Decimal or ColumnType.Percent)
            .Where(x => !x.c.Name.EndsWith("Id", StringComparison.Ordinal) && !x.c.Name.EndsWith("Code", StringComparison.Ordinal))
            .Select(x => x.i).ToArray();
        var hidden = 0;
        for (var r = 0; r < rows.Count; r++)
        {
            var small = counts.Any(i => rows[r][i] is { } v && Convert.ToDecimal(v) is > 0 and var n && n < min);
            if (!small) continue;
            var copy = rows[r].ToArray();
            foreach (var i in figures) copy[i] = null;
            rows[r] = copy;
            hidden++;
        }
        return hidden == 0 ? null : hidden;
    }
}
