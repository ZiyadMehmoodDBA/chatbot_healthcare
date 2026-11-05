using System.Globalization;
using System.Text.RegularExpressions;

namespace AiChatbot.Reporting;

public sealed record DateRange(DateOnly Start, DateOnly End, string Phrase)
{
    public int Days => End.DayNumber - Start.DayNumber + 1;
    public string Label => $"{Start:yyyy-MM-dd} to {End:yyyy-MM-dd}";
}

// Turns the words people use for time ("last month", "QTD", "Q1 2026", "April", "last 30 days") into exact dates, and
// finds the period to compare against. Calendar-based: the fiscal calendar is not defined yet. Deterministic and
// independent of any model, so "this month versus last month" always means the same dates.
public static partial class Periods
{
    private static readonly string[] MonthNames =
        ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];

    // Phrases that mean "against the same period a year earlier" or "against the period just before"
    [GeneratedRegex(@"(?<![a-z])(?:the\s+)?same\s+(?:period|month|quarter|week)\s+(?:last\s+year|a\s+year\s+ago)|(?<![a-z])year[- ]over[- ]year(?![a-z])|(?<![a-z])yoy(?![a-z])|(?<![a-z])a\s+year\s+ago(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex YearAgoMarker();

    [GeneratedRegex(@"(?<![a-z])(?:the\s+)?(?:previous|prior|preceding)\s+(?:period|month|quarter|year|week)(?![a-z])|(?<![a-z])the\s+(?:month|quarter|year|week)\s+before(?![a-z])|(?<![a-z])month[- ]over[- ]month(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex PreviousMarker();

    [GeneratedRegex(@"(?<![a-z])(?:last|past)\s+(\d{1,3})\s+(day|week|month)s?(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex LastN();

    [GeneratedRegex(@"(?<![a-z0-9])q([1-4])(?:\s*(?:of\s*)?(20\d{2}))?(?![a-z0-9])", RegexOptions.IgnoreCase)]
    private static partial Regex QuarterNumber();

    [GeneratedRegex(@"(?<![a-z])(jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|june?|july?|aug(?:ust)?|sep(?:t(?:ember)?)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)(?![a-z])(?:\s*,?\s*(20\d{2}))?", RegexOptions.IgnoreCase)]
    private static partial Regex MonthName();

    [GeneratedRegex(@"(?<![a-z])(today|yesterday|this\s+week|last\s+week|this\s+month|month[- ]to[- ]date|mtd|last\s+month|this\s+quarter|quarter[- ]to[- ]date|qtd|last\s+quarter|this\s+year|year[- ]to[- ]date|ytd|last\s+year)(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex Named();

    [GeneratedRegex(@"(?<![a-z0-9])(20\d{2})(?![a-z0-9])")]
    private static partial Regex YearOnly();

    public sealed record Found(DateRange Range, int Index, int Length);

    public sealed record Comparison(DateRange Current, DateRange Prior);

    // Words that make "may" and "march" mean the month and not a verb
    private static readonly string[] MonthContext = ["in", "for", "of", "during", "from", "to", "and", "vs", "versus", "than", "against", "through", "since", "compare", "between", "by"];

    // Every period named in the text, in reading order. Overlapping matches keep the first, most specific one.
    public static IReadOnlyList<Found> Find(string text, DateOnly today)
    {
        var found = new List<Found>();
        void Add(Match m, DateRange? r)
        {
            if (r is null) return;
            if (found.Any(f => m.Index < f.Index + f.Length && f.Index < m.Index + m.Length)) return;
            found.Add(new Found(r, m.Index, m.Length));
        }

        foreach (Match m in LastN().Matches(text))
        {
            var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (n < 1) continue;
            var unit = m.Groups[2].Value.ToLowerInvariant();
            var start = unit switch
            {
                "week" => today.AddDays(-(n * 7 - 1)),
                "month" => today.AddMonths(-n).AddDays(1),
                _ => today.AddDays(-(n - 1)),
            };
            Add(m, new DateRange(start, today, m.Value));
        }
        foreach (Match m in QuarterNumber().Matches(text))
        {
            var q = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var year = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : today.Year;
            // "Q4" said in February means last year's Q4 when there is no year
            if (!m.Groups[2].Success && QuarterStart(year, q) > today) year--;
            Add(m, Quarter(year, q, m.Value));
        }
        foreach (Match m in MonthName().Matches(text))
        {
            var month = MonthIndex(m.Groups[1].Value);
            if (month == 0) continue;
            var hasYear = m.Groups[2].Success;
            if (m.Groups[1].Value.Equals("may", StringComparison.OrdinalIgnoreCase) && !hasYear && !HasMonthContext(text, m.Index)) continue;
            var year = hasYear ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : today.Year;
            if (!hasYear && new DateOnly(year, month, 1) > today) year--;
            Add(m, FullMonth(year, month, m.Value));
        }
        foreach (Match m in Named().Matches(text))
            Add(m, Resolve(m.Value, today));
        foreach (Match m in YearOnly().Matches(text))
        {
            var y = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            Add(m, new DateRange(new DateOnly(y, 1, 1), new DateOnly(y, 12, 31), m.Value));
        }
        return found.OrderBy(f => f.Index).ToList();
    }

    // The two periods to compare, or null when the text names none. Newer first is not assumed: of two named periods
    // the later one is "current". One period is compared with the one before it, or a year earlier when asked.
    public static Comparison? Compare(string text, DateOnly today)
    {
        var yearAgo = YearAgoMarker().IsMatch(text);
        var cleaned = YearAgoMarker().Replace(PreviousMarker().Replace(text, " "), " ");
        var ranges = Find(cleaned, today).Select(f => f.Range).ToList();
        if (ranges.Count >= 2)
        {
            var ordered = ranges.Take(2).OrderByDescending(r => r.Start).ThenByDescending(r => r.End).ToList();
            return new Comparison(ordered[0], ordered[1]);
        }
        if (ranges.Count == 1)
        {
            var current = ranges[0];
            return new Comparison(current, yearAgo ? SameLastYear(current) : Previous(current));
        }
        return null;
    }

    public static bool HasComparisonMarker(string text) => YearAgoMarker().IsMatch(text) || PreviousMarker().IsMatch(text);

    public static DateRange? Resolve(string phrase, DateOnly today)
    {
        var p = Regex.Replace(phrase.Trim().ToLowerInvariant(), @"[\s-]+", " ");
        switch (p)
        {
            case "today": return new DateRange(today, today, phrase);
            case "yesterday": return new DateRange(today.AddDays(-1), today.AddDays(-1), phrase);
            case "this week":
            {
                var start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                return new DateRange(start, today, phrase);
            }
            case "last week":
            {
                var thisMonday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                return new DateRange(thisMonday.AddDays(-7), thisMonday.AddDays(-1), phrase);
            }
            case "this month" or "month to date" or "mtd":
                return new DateRange(new DateOnly(today.Year, today.Month, 1), today, phrase);
            case "last month":
            {
                var first = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
                return FullMonth(first.Year, first.Month, phrase);
            }
            case "this quarter" or "quarter to date" or "qtd":
                return new DateRange(QuarterStart(today.Year, QuarterOf(today)), today, phrase);
            case "last quarter":
            {
                var start = QuarterStart(today.Year, QuarterOf(today)).AddMonths(-3);
                return Quarter(start.Year, QuarterOf(start), phrase);
            }
            case "this year" or "year to date" or "ytd":
                return new DateRange(new DateOnly(today.Year, 1, 1), today, phrase);
            case "last year":
                return new DateRange(new DateOnly(today.Year - 1, 1, 1), new DateOnly(today.Year - 1, 12, 31), phrase);
            default:
                return null;
        }
    }

    // The period of the same shape just before: previous month for a month, previous quarter for a quarter, the same
    // days of the previous month for month-to-date, and so on. Other lengths use the equal span immediately before.
    public static DateRange Previous(DateRange r)
    {
        var start = r.Start;
        var endsOnMonthEnd = r.End == new DateOnly(r.End.Year, r.End.Month, DateTime.DaysInMonth(r.End.Year, r.End.Month));
        if (start.Day == 1 && endsOnMonthEnd)
        {
            var months = (r.End.Year - start.Year) * 12 + r.End.Month - start.Month + 1;
            return new DateRange(start.AddMonths(-months), start.AddDays(-1), "the period before");
        }
        if (start.Month == 1 && start.Day == 1)
            return new DateRange(start.AddYears(-1), CapToMonthEnd(r.End.AddYears(-1)), "the period before");
        if (start.Day == 1 && start.Month % 3 == 1)
            return CappedShift(r, 3);
        if (start.Day == 1)
            return CappedShift(r, 1);
        return new DateRange(start.AddDays(-r.Days), start.AddDays(-1), "the period before");
    }

    public static DateRange SameLastYear(DateRange r)
        => new(r.Start.AddYears(-1), CapToMonthEnd(r.End.AddYears(-1)), "the same period a year earlier");

    private static DateRange CappedShift(DateRange r, int months)
    {
        var start = r.Start.AddMonths(-months);
        var end = start.AddDays(r.Days - 1);
        var limit = r.Start.AddDays(-1);
        return new DateRange(start, end > limit ? limit : end, "the period before");
    }

    private static DateOnly CapToMonthEnd(DateOnly d) => d;

    private static DateRange FullMonth(int year, int month, string phrase)
        => new(new DateOnly(year, month, 1), new DateOnly(year, month, DateTime.DaysInMonth(year, month)), phrase);

    private static DateRange Quarter(int year, int q, string phrase)
    {
        var start = QuarterStart(year, q);
        return new DateRange(start, start.AddMonths(3).AddDays(-1), phrase);
    }

    private static DateOnly QuarterStart(int year, int q) => new(year, (q - 1) * 3 + 1, 1);

    private static int QuarterOf(DateOnly d) => (d.Month - 1) / 3 + 1;

    private static int MonthIndex(string name)
    {
        var n = name.ToLowerInvariant();
        for (var i = 0; i < MonthNames.Length; i++)
            if (MonthNames[i].StartsWith(n[..Math.Min(3, n.Length)], StringComparison.Ordinal) && MonthNames[i].StartsWith(n, StringComparison.Ordinal))
                return i + 1;
        // "sept"
        return n == "sept" ? 9 : 0;
    }

    private static bool HasMonthContext(string text, int index)
    {
        var before = text[..index].TrimEnd().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return before.Length > 0 && MonthContext.Contains(before[^1].Trim(',', '.', '?').ToLowerInvariant());
    }
}
