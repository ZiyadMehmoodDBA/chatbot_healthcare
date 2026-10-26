using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace AiChatbot.Safety;

// Text with identifiers replaced by tokens. KnownTokens are the vault tokens present in Text.
public sealed record TokenizedText(string Text, IReadOnlyList<string> KnownTokens);

// The only place identifiers are swapped for tokens and back. Frontend and prompts never do this.
public interface ITokenizationService
{
    TokenizedText Tokenize(Guid conversationId, string text);
    string Detokenize(Guid conversationId, string text);
    bool TryResolveAccount(Guid conversationId, string tokenOrValue, out long account);
}

// Token map lives in server memory per conversation; swap for a protected store when persistence is added.
public sealed partial class TokenizationService : ITokenizationService
{
    private sealed class Vault
    {
        public readonly Dictionary<string, long> ByToken = [];
        public readonly Dictionary<long, string> ByValue = [];
    }

    private readonly ConcurrentDictionary<Guid, Vault> _vaults = new();

    // Labelled identifiers only: "account 12345", "acct #12345", "patient: 12345", "MRN 12345", "case 12345", "#12345",
    // plus comma continuations ("accounts 1111, 2222").
    [GeneratedRegex(
        @"(?<label>(?:\b(?:accounts?|acct|patient|mrn|case)\b\.?(?:\s*(?:number|no|id|is)(?![a-z]))*[\s:=#\-(]*|#\s*))" +
        @"(?<num>(?<![0-9])[0-9]{4,12})(?![0-9])(?<cont>(?:\s*,\s*[0-9]{4,12}(?![0-9]))*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Labelled();

    [GeneratedRegex(@"(?<![0-9])[0-9]{4,12}(?![0-9])")]
    private static partial Regex LongNumber();

    [GeneratedRegex(@"[0-9]{4,12}")]
    private static partial Regex Digits();

    [GeneratedRegex(@"\{ACCT_[0-9]+\}")]
    private static partial Regex TokenPattern();

    public TokenizedText Tokenize(Guid conversationId, string text)
    {
        var vault = _vaults.GetOrAdd(conversationId, _ => new Vault());
        lock (vault)
        {
            var result = Labelled().Replace(text, m =>
                m.Groups["label"].Value + Mint(vault, m.Groups["num"].Value)
                + Digits().Replace(m.Groups["cont"].Value, d => Mint(vault, d.Value)));

            // Later bare mentions of a value already in the vault
            result = LongNumber().Replace(result, m =>
                long.TryParse(m.Value, out var v) && vault.ByValue.TryGetValue(v, out var t) ? t : m.Value);

            var known = TokenPattern().Matches(result).Select(m => m.Value).Where(vault.ByToken.ContainsKey).Distinct().ToList();
            return new TokenizedText(result, known);
        }
    }

    public string Detokenize(Guid conversationId, string text)
    {
        if (!_vaults.TryGetValue(conversationId, out var vault)) return text;
        lock (vault)
            return TokenPattern().Replace(text, m => vault.ByToken.TryGetValue(m.Value, out var v) ? v.ToString() : m.Value);
    }

    // Accepts a token, or digits that are already in this conversation's vault. Unknown values never resolve.
    public bool TryResolveAccount(Guid conversationId, string tokenOrValue, out long account)
    {
        account = 0;
        if (!_vaults.TryGetValue(conversationId, out var vault)) return false;
        var s = tokenOrValue.Trim();
        lock (vault)
        {
            if (vault.ByToken.TryGetValue(s, out account)) return true;
            return long.TryParse(s, out var v) && vault.ByValue.ContainsKey(v) && (account = v) == v;
        }
    }

    private static string Mint(Vault vault, string digits)
    {
        if (!long.TryParse(digits, out var value)) return digits;
        if (vault.ByValue.TryGetValue(value, out var existing)) return existing;
        var token = $"{{ACCT_{vault.ByToken.Count + 1}}}";
        vault.ByToken[token] = value;
        vault.ByValue[value] = token;
        return token;
    }
}
