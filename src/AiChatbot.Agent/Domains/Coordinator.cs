using System.Text.RegularExpressions;
using AiChatbot.Reporting;

namespace AiChatbot.Agent.Domains;

public enum PlanKind
{
    // One domain matched: only its tools are offered
    Single,
    // Several domains matched: each part of the question is answered with its own tools
    Multi,
    // Nothing specific matched: every tool the actor may use is offered and the model decides
    Open,
    // A known report area that is not built yet, or nothing the actor may use: answered without a model call
    Unsupported,
}

// The coordinator's decision for one question. Bounded: the offered tools are a subset of what the actor may use.
public sealed record CoordinatorPlan(
    PlanKind Kind, IReadOnlyList<IDomainModule> Modules, string Reason, string? Reply = null)
{
    public IReadOnlyList<string> Domains => Modules.Select(m => m.Domain).ToList();
    public IReadOnlyList<string> ToolNames => Modules.SelectMany(m => m.Tools).Select(t => t.Name).ToList();
}

// Routes a question to domain tool groups before any model call. Deterministic on purpose: it is free, it is the same
// every time, and it is easy to test. When it cannot tell, it offers every tool the actor may use and lets the model
// choose; the tool boundary still enforces everything.
public static class Coordinator
{
    public const int MaxToolCalls = 6;

    // Areas in the report catalogue that have no tools yet. Asking about them gets an honest answer, not a guess.
    private static readonly (string Area, string[] Words)[] NotBuilt =
    [
        ("denial and rejection analysis", ["denial", "denied", "deny", "rejection", "rejected", "appeal"]),
        ("claim status and submission reports", ["claim status", "claims status", "submission", "resubmission", "days to bill", "clean claim"]),
        ("CPT procedure performance", ["cpt", "procedure code"]),
        ("facility and sales representative reports", ["sales rep", "facility performance"]),
        ("claim interest reports", ["interest"]),
    ];

    // Patient-level requests are checked first: "account balance" must not be answered by the payer A/R report
    private static readonly string[] PatientLevel = ["statement", "patient account", "account balance", "patient balance", "ledger"];

    public static CoordinatorPlan Plan(string question, Actor actor, IReadOnlyList<IDomainModule> all)
    {
        var allowed = all.Where(m => m.Allows(actor)).ToList();
        if (allowed.Count == 0)
            return new(PlanKind.Unsupported, [], "no allowed domain",
                "Your role can't run any of the reports available here.");

        var text = question.ToLowerInvariant();
        if (PatientLevel.Any(w => HasWord(text, w)))
            return new(PlanKind.Unsupported, [], "not built: patient account statements",
                $"I can't report on patient account statements yet. I can help with {Describe(allowed)}.");

        var hits = allowed.Where(m => m.Keywords.Any(k => HasWord(text, k))).ToList();

        if (hits.Count == 1)
            return new(PlanKind.Single, hits, $"matched {hits[0].Domain}");
        if (hits.Count > 1)
            return new(PlanKind.Multi, hits, $"matched {string.Join(" and ", hits.Select(h => h.Domain))}");

        foreach (var (area, words) in NotBuilt)
            if (words.Any(w => HasWord(text, w)))
                return new(PlanKind.Unsupported, [], $"not built: {area}",
                    $"I can't report on {area} yet. I can help with {Describe(allowed)}.");

        return new(PlanKind.Open, allowed, "no domain matched; offering all allowed tools");
    }

    public static string Describe(IReadOnlyList<IDomainModule> modules)
        => string.Join(" and ", modules.Select(m => m.Description));

    // Whole-word match so "era" does not fire inside "operations"; multi-word phrases match as written
    private static bool HasWord(string text, string word)
        => Regex.IsMatch(text, $@"(?<![a-z0-9]){Regex.Escape(word)}(?![a-z0-9])");
}
