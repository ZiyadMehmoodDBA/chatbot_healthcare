using System.Text;
using System.Text.RegularExpressions;

namespace AiChatbot.Agent.Conversation;

// Casual typing ("hw much did we colect in may", "denails by paer") should route like the clean wording. Only words of
// five letters or more that are not already known are changed, only to the single nearest known report word, and only
// within one edit (two for long words). Numbers, identifiers and anything ambiguous are left exactly as typed.
public static partial class TypoFixer
{
    [GeneratedRegex(@"[A-Za-z]{4,}")]
    private static partial Regex WordRx();

    // Everyday words that must never be bent into a report word
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "which", "where", "while", "about", "would", "could", "should", "their", "there", "these", "those", "other", "first", "again",
        "after", "before", "since", "under", "above", "below", "show", "think", "never", "every", "month", "months", "years",
        "paid", "paying", "this", "that", "with", "from", "much", "many", "last", "next", "type", "each", "year", "week", "days", "does", "were", "have", "been", "than", "then", "what", "when", "your", "over", "into", "claim", "total", "money", "doing", "going", "never", "thanks", "thank", "please", "never",
    };

    public static string Fix(string text, IEnumerable<string> vocabulary)
    {
        var vocab = vocabulary.SelectMany(v => v.Split([' ', '-', '/'], StringSplitOptions.RemoveEmptyEntries))
            .Select(v => v.ToLowerInvariant()).Where(v => v.Length >= 5 && v.All(char.IsLetter)).Distinct().ToList();
        if (vocab.Count == 0) return text;
        var known = new HashSet<string>(vocab);
        return WordRx().Replace(text, m =>
        {
            var w = m.Value.ToLowerInvariant();
            if (known.Contains(w) || Common.Contains(w) || (w.Length == 4 && !w.Any(char.IsLetter))) return m.Value;
            var limit = w.Length >= 8 ? 2 : 1;
            var shortWord = w.Length == 4; // a four-letter word may only gain one letter to reach a report word
            string? best = null;
            var bestD = int.MaxValue;
            var tie = false;
            foreach (var v in vocab)
            {
                if (Math.Abs(v.Length - w.Length) > limit) continue;
                if (shortWord && v.Length != 5) continue;
                var d = Distance(w, v, limit);
                if (d > limit) continue;
                if (d < bestD) { best = v; bestD = d; tie = false; }
                else if (d == bestD) tie = true;
            }
            return best is null || tie ? m.Value : best;
        });
    }

    // Damerau-Levenshtein (adjacent swaps count as one edit), giving up once past the limit
    internal static int Distance(string a, string b, int limit)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var rowMin = int.MaxValue;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                rowMin = Math.Min(rowMin, d[i, j]);
            }
            if (rowMin > limit) return limit + 1;
        }
        return d[a.Length, b.Length];
    }
}
