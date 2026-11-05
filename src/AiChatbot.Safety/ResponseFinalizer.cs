using System.Text;
using System.Text.RegularExpressions;

namespace AiChatbot.Safety;

public static partial class ResponseFinalizer
{
    public const string Unavailable = "[unavailable]";

    [GeneratedRegex(@"\{([A-Z][A-Z0-9_]{1,40})\}")]
    private static partial Regex Placeholder();

    // Any Unicode decimal digits, with thousands separators and decimals kept together ("1,234.5" is one number)
    [GeneratedRegex(@"\p{Nd}+(?:[.,]\p{Nd}+)*")]
    private static partial Regex Number();

    [GeneratedRegex(@"\p{Nd}")]
    private static partial Regex AnyDigit();

    [GeneratedRegex(@"(?<![a-z])(?:zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|sixteen|seventeen|eighteen|nineteen|twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety|hundred|hundreds|thousand|thousands|million|millions|billion|billions|dozen|half|quarter)(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex NumberWord();

    // Words that state an amount ("nine million"). Small words such as "one payer" are ordinary prose and stay allowed.
    [GeneratedRegex(@"(?<![a-z])(?:hundreds?|thousands?|millions?|billions?|trillions?|dozens?)(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex MagnitudeWord();

    // Whole number token with thousands separators removed and non-ASCII digits turned into 0-9
    private static string Normalize(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            if (ch == ',') continue;
            sb.Append(char.IsDigit(ch) ? (char)('0' + (int)char.GetNumericValue(ch)) : ch);
        }
        return sb.ToString();
    }

    // True when the text claims a quantity at all (digits in any script, or number words). Used when no report ran:
    // then the model has no figure it could legitimately state.
    public static bool ContainsNumberClaim(string text) => AnyDigit().IsMatch(text) || NumberWord().IsMatch(text);

    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
        => Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : Unavailable);

    // True when the text (outside placeholders) holds a number that no value shown to the model contains.
    public static bool HasInventedNumbers(string modelText, IEnumerable<string> shownValues)
    {
        var allowed = shownValues.SelectMany(v => Number().Matches(v).Select(m => Normalize(m.Value))).ToHashSet();
        var stripped = Placeholder().Replace(modelText, " ");
        if (MagnitudeWord().IsMatch(stripped)) return true;
        return Number().Matches(stripped).Any(m => !allowed.Contains(Normalize(m.Value)));
    }
}

// Applies a fill function to streamed text without splitting a {PLACEHOLDER} across chunks.
public sealed class StreamingFinalizer(Func<string, string> fill)
{
    private const int MaxPlaceholder = 48;
    private readonly StringBuilder _pending = new();

    public string Push(string chunk)
    {
        _pending.Append(chunk);
        var s = _pending.ToString();
        var open = s.LastIndexOf('{');
        var hold = open >= 0 && s.IndexOf('}', open) < 0 && s.Length - open < MaxPlaceholder ? open : s.Length;
        _pending.Clear().Append(s, hold, s.Length - hold);
        return hold == 0 ? "" : fill(s[..hold]);
    }

    public string Flush()
    {
        var s = _pending.ToString();
        _pending.Clear();
        return s.Length == 0 ? "" : fill(s);
    }
}
