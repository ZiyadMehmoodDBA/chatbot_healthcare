using System.Text.RegularExpressions;
using AiChatbot.Agent.Domains;
using AiChatbot.Reporting;

namespace AiChatbot.Agent.Conversation;

// A report question that names no period gets a short question back, decided in code before any model call, instead
// of a guess (or a table for a period with no data). The user's answer is joined to the original question and decided
// again. Only things the reports can really do are offered: a period, and a split by payer, practice or month. A filter
// to one practice or payer does not exist (the model cannot choose one), so it is never offered.
public static partial class ClarifyPlanner
{
    // Domains whose reports cover a period. A/R is a balance as of a day, so it never needs one.
    private static readonly HashSet<string> PeriodDomains = ["payments", "claims", "submissions", "charges", "denials"];

    [GeneratedRegex(@"(?<![0-9])20[0-9]{2}-[0-9]{2}-[0-9]{2}(?![0-9])")]
    private static partial Regex IsoDate();

    // "same for April", "also by payer": the earlier report's period is reused (see DecisionEngine.ContextFor)
    [GeneratedRegex(@"(?<![a-z])(same|again|also|now|too|instead|those|that)(?![a-z])", RegexOptions.IgnoreCase)]
    private static partial Regex FollowUp();

    public static bool HasPeriod(string text, DateOnly today)
        => Periods.Find(text, today).Count > 0 || IsoDate().IsMatch(text);

    // A message that is only a period ("May", "last quarter") with no report named. Returns the phrase to reuse
    // ("in May" for a bare month), or null. A "same for ..." follow-up is not a bare period.
    public static string? PeriodOnly(string text, bool hasReportWords, DateOnly today)
    {
        var t = text.Trim().TrimEnd('.', '?', '!');
        if (hasReportWords || FollowUp().IsMatch(t)) return null;
        if (t.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length > 3) return null;
        if (HasPeriod(t, today)) return t;
        return HasPeriod($"in {t}", today) ? $"in {t}" : null;
    }

    // Words that leave two different reports possible. One focused question back beats a guess.
    [GeneratedRegex(@"\b(?:outstanding|open|unpaid|owed|owing)\s+(?:balances?|amounts?|money)\b|\bwho\s+owes\b|\bhow\s+much\s+(?:is\s+)?(?:owed|outstanding)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BalanceRx();

    [GeneratedRegex(@"\b(?:insurance|payer|a/r|ar|aging|account|patient|patients)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BalanceOwnerRx();

    [GeneratedRegex(@"\bhow\s+much\s+(?:did\s+we|have\s+we|do\s+we)\s+(?:bill|billed)\b|\b(?:billing|billed)\s+(?:total|amount|volume)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BillRx();

    [GeneratedRegex(@"\b(?:charges?|charged|payments?|paid|collect\w*|received|claims?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BillClearRx();

    // "outstanding balances": insurance A/R or one patient account? "how much did we bill": charges or payments?
    public static ReplyDecision? Ambiguous(string text)
    {
        if (BalanceRx().IsMatch(text) && !BalanceOwnerRx().IsMatch(text))
            return new("Do you mean what insurance payers owe (A/R by age), or one patient's account? I can't total patient balances yet, only look up a single account.",
                TurnKind.Clarification, "clarify: balance owner",
                Chips: [new Chip("Insurance A/R", "insurance a/r aging"), new Chip("One patient account", "patient balance")]);
        if (BillRx().IsMatch(text) && !BillClearRx().IsMatch(text))
            return new("Billed means different things. Do you want the charges we billed, or the payments we received?",
                TurnKind.Clarification, "clarify: billed vs paid",
                Chips: [new Chip("Charges billed", "charges by practice"), new Chip("Payments received", "how much did we collect")]);
        return null;
    }

    public static ReplyDecision AskReportFor(string phrase)
        => new($"Happy to, which report would you like for {(phrase.StartsWith("in ") ? phrase[3..] : phrase)}? Payments, claims, denials or submissions?",
            TurnKind.Clarification, "clarify: period without a report",
            Chips: [new Chip("Payments", $"payments {phrase}"), new Chip("Claims", $"claims processed {phrase}"),
                    new Chip("Denials", $"denials {phrase}"), new Chip("Submissions", $"submissions {phrase}")]);

    public static string AskPeriodFor(CoordinatorPlan plan)
    {
        var split = plan.Modules.Any(m => m.Domain is "payments" or "denials" or "claims" or "submissions")
            ? " I can also split it by payer, practice or month."
            : "";
        return $"Sure, which period should I look at? Something like 'last month', 'May', 'Q1' or 'year to date' works.{split}";
    }

    // null = nothing to ask. Otherwise a question that waits for a period.
    public static ReplyDecision? Plan(string text, CoordinatorPlan plan, SessionSnapshot session, DateOnly today)
    {
        if (plan.Kind is not (PlanKind.Single or PlanKind.Multi)) return null;
        if (!plan.Modules.Any(m => PeriodDomains.Contains(m.Domain))) return null;
        if (HasPeriod(text, today)) return null;
        if (session.LastReport is not null && FollowUp().IsMatch(text)) return null;

        var ask = AskPeriodFor(plan);
        return new ReplyDecision(ask, TurnKind.Clarification, "clarify: period missing",
            PendingAction.Set, new PendingQuestion(PendingKind.ReportPeriod, ask, text), Chips: AssistBuilder.PeriodChips);
    }
}
