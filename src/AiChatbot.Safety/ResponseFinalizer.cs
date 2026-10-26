using System.Text;
using System.Text.RegularExpressions;

namespace AiChatbot.Safety;

public static partial class ResponseFinalizer
{
    public const string Unavailable = "[unavailable]";

    [GeneratedRegex(@"\{([A-Z][A-Z0-9_]{1,40})\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"[0-9]+(?:\.[0-9]+)?")]
    private static partial Regex Number();

    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
        => Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : Unavailable);

    // True when the text (outside placeholders) holds a number that no value shown to the model contains.
    public static bool HasInventedNumbers(string modelText, IEnumerable<string> shownValues)
    {
        var allowed = shownValues.SelectMany(v => Number().Matches(v).Select(m => m.Value)).ToHashSet();
        var stripped = Placeholder().Replace(modelText, " ");
        return Number().Matches(stripped).Any(m => !allowed.Contains(m.Value));
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
