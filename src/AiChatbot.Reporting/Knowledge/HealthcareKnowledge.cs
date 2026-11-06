using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AiChatbot.Reporting;

public sealed record GlossaryTerm(string Term, string[] Aliases, string Definition, string Basis, string Limits, string Status);

public sealed record Playbook(string Id, string Title, string[] Checks, string[] CannotEstablish, string[] Next);

// Business knowledge the assistant explains from, loaded from embedded JSON: what words mean, what the data cannot
// establish, and what to look at next. It holds no financial figures and never changes what a report returns.
public sealed partial class HealthcareKnowledge
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly List<GlossaryTerm> _terms;
    private readonly List<Playbook> _playbooks;

    private HealthcareKnowledge(List<GlossaryTerm> terms, List<Playbook> playbooks) => (_terms, _playbooks) = (terms, playbooks);

    private static readonly Lazy<HealthcareKnowledge> SharedInstance = new(LoadEmbedded);
    public static HealthcareKnowledge Shared => SharedInstance.Value;

    public IReadOnlyList<GlossaryTerm> Terms => _terms;

    public static HealthcareKnowledge LoadEmbedded()
    {
        var asm = typeof(HealthcareKnowledge).Assembly;
        return new(Read<GlossaryFile>(asm, "glossary.json").Terms, Read<PlaybookFile>(asm, "playbooks.json").Playbooks);
    }

    private static T Read<T>(Assembly asm, string name)
    {
        using var s = asm.GetManifestResourceStream(name) ?? throw new InvalidOperationException($"Missing resource {name}");
        return JsonSerializer.Deserialize<T>(s, Json)!;
    }

    private sealed record GlossaryFile(List<GlossaryTerm> Terms);
    private sealed record PlaybookFile(List<Playbook> Playbooks);

    public Playbook? Playbook(string id) => _playbooks.FirstOrDefault(p => p.Id == id);

    // "what is a denial", "define write-off", "what does contractual adjustment mean". Questions about our own figures
    // ("what is our net collection rate") are not definitions and are left to the report flow.
    [GeneratedRegex(@"^\s*(?:what\s+(?:is|are|does)|what['’]?s|define|explain|meaning\s+of|tell\s+me\s+about)\s+(?:an?\s+|the\s+)?(?<term>[a-z0-9/ '’-]{2,40}?)(?:\s+mean|\s+means)?\s*[?.!]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DefineRx();

    [GeneratedRegex(@"\b(?:our|my|we|us|your|this|last|next|current|total)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Personal();

    public GlossaryTerm? Define(string text)
    {
        var m = DefineRx().Match(text);
        if (!m.Success) return null;
        var asked = m.Groups["term"].Value.Trim().TrimEnd('s', 'S').ToLowerInvariant();
        var raw = m.Groups["term"].Value.Trim();
        if (Personal().IsMatch(raw)) return null;
        string Norm(string s) => s.ToLowerInvariant().Trim().TrimEnd('s');
        return _terms.FirstOrDefault(t => Norm(t.Term) == asked || t.Aliases.Any(a => Norm(a) == asked));
    }

    public static string Explain(GlossaryTerm t)
        => $"{Cap(t.Term)}: {t.Definition} In this app: {t.Basis} {t.Limits} (Status: {t.Status.ToLowerInvariant()}.)";

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
