using System.Text.RegularExpressions;

namespace AiChatbot.Safety;

// Fail-closed check on tokenized text that is about to leave the network. A mitigation, not de-identification:
// a single capitalised name, lower-case names, spelled-out digits and URLs still pass. False rejects (for example
// two-word proper names) are the accepted cost; users rephrase.
public static partial class ExternalInputScreen
{
    public const string Withheld = "[withheld]";

    public const string BlockedReply =
        "For privacy, I can't send names or unlabelled numbers to the external model. " +
        "Please rephrase without names, and write identifiers like 'account 12345'.";

    private const int MaxLength = 20_000;

    private static readonly HashSet<int> AllowedPunct = [0x2018, 0x2019, 0x201C, 0x201D, 0x2013, 0x2014, 0x2026];

    private static readonly HashSet<string> Salutations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr", "mrs", "ms", "miss", "mx", "dr", "doctor", "prof", "sir", "madam", "mister", "patient", "pt",
    };

    // A capitalised pair is ignored only when BOTH words are here ("What Is", "Show Me", "North Campus").
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "I", "I'm", "I’m", "I'd", "I've", "I'll", "It", "This", "That", "Our", "My", "The", "A", "An", "Me", "Us", "We",
        "Is", "What", "What's", "What’s", "Whats", "Show", "How", "Can", "Does", "Do", "Please", "Tell", "List", "Give", "Which",
        "Why", "When", "Who", "Are", "Was", "Were", "Will", "Could", "Would", "And", "Or", "For", "Of", "In", "On", "To",
        "All", "Any", "Each", "Every", "Last", "Next", "Total", "Average", "Avg",
        "Account", "Report", "Reports", "Summary", "Metrics", "Facility", "Hospital", "Clinic", "Campus", "Ward", "Unit",
        "Incident", "Incidents", "Quality", "Readmission", "Readmissions", "Admission", "Admissions", "Length", "Stay",
        "Rate", "Count", "Visit", "Visits", "Open", "North", "South", "East", "West",
    };

    [GeneratedRegex(@"[0-9]{4,}")]
    private static partial Regex LongDigits();

    [GeneratedRegex(@"(?:[0-9][\s.\-/()]*){7,}")]
    private static partial Regex PhoneLike();

    [GeneratedRegex(@"\b(?:dob|date\s+of\s+birth|birth\s*date|birthday|born)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BirthWords();

    [GeneratedRegex(@"\b[0-9]{1,2}[/\-.][0-9]{1,2}[/\-.][0-9]{2,4}\b")]
    private static partial Regex NumericDate();

    [GeneratedRegex(@"\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\.?\s+[0-9]{1,2}\b", RegexOptions.IgnoreCase)]
    private static partial Regex MonthDay();

    [GeneratedRegex(@"\b[A-Z][a-z][A-Za-z'’]*\b|\b[A-Z]\.")]
    private static partial Regex CapWord();

    [GeneratedRegex(@"\b(?<sal>[A-Za-z]+)\b\.?\s+(?<next>[A-Z][a-z][A-Za-z'’]*)")]
    private static partial Regex SalutationName();

    [GeneratedRegex(@"^[\s,.]{1,3}$")]
    private static partial Regex WordGap();

    public static bool IsSafe(string tokenizedText, IEnumerable<string> knownTokens)
    {
        if (tokenizedText.Length > MaxLength || !AllCharsAllowed(tokenizedText)) return false;

        var text = tokenizedText;
        foreach (var t in knownTokens) text = text.Replace(t, " ");

        if (LongDigits().IsMatch(text) || text.Contains('@') || PhoneLike().IsMatch(text)) return false;
        if (BirthWords().IsMatch(text) || NumericDate().IsMatch(text) || MonthDay().IsMatch(text)) return false;
        return !LooksLikeName(text);
    }

    // Printable ASCII (minus < > \), tab/CR/LF, Latin letters U+00C0-U+024F, curly quotes, dashes, ellipsis.
    // Rejects non-ASCII digits, invisible/control characters and lone surrogates.
    private static bool AllCharsAllowed(string s)
    {
        foreach (var c in s)
        {
            var ok = c is '\t' or '\r' or '\n'
                || (c is >= ' ' and <= '~' && c is not ('<' or '>' or '\\'))
                || c is >= 'À' and <= 'ɏ'
                || AllowedPunct.Contains(c);
            if (!ok) return false;
        }
        return true;
    }

    private static bool LooksLikeName(string text)
    {
        foreach (Match m in SalutationName().Matches(text))
            if (Salutations.Contains(m.Groups["sal"].Value) && !StopWords.Contains(m.Groups["next"].Value))
                return true;

        var words = CapWord().Matches(text);
        for (var i = 0; i + 1 < words.Count; i++)
        {
            var a = words[i];
            var b = words[i + 1];
            var gap = text.Substring(a.Index + a.Length, b.Index - a.Index - a.Length);
            if (!WordGap().IsMatch(gap)) continue;
            if (!(StopWords.Contains(a.Value) && StopWords.Contains(b.Value))) return true;
        }
        return false;
    }
}
