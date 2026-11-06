using System.Text.RegularExpressions;
using AiChatbot.Agent.Domains;
using AiChatbot.Reporting;
using AiChatbot.Safety;

namespace AiChatbot.Agent.Conversation;

public enum PendingAction { Keep, Clear, Set }

public abstract record Decision(string Reason);

// Answer now from memory or with a clarifying question. No model call, no data call.
public sealed record ReplyDecision(
    string Text,
    TurnKind Kind,
    string Reason,
    PendingAction Pending = PendingAction.Keep,
    PendingQuestion? NewPending = null,
    bool ResetContext = false,
    IReadOnlyList<Chip>? Chips = null) : Decision(Reason);

// Look up one patient account. The server does it, not the model, so no patient value can reach a model.
public sealed record PatientDecision(string Token, long Account, bool SummaryOnly, string Reason) : Decision(Reason);

// Everything else: the coordinator's plan drives the model and its tools (or an honest "not built" reply).
public sealed record ModelDecision(CoordinatorPlan Plan, bool Brief, string? Context, string Reason) : Decision(Reason);

// The decision system for one question, in a fixed order, decided before any model call:
//   1. cancel / start over        2. questions about the conversation (first, last, recap, summarize, repeat, help)
//   3. an answer to our own pending question (account number, yes/no)
//   4. patient account requests: ask for what is missing, never guess a patient
//   5. report questions: the coordinator's plan, plus follow-up context for the model
// Deterministic on purpose: the same input gives the same decision, every decision is testable offline, and the
// patient path never passes patient data through a model.
public static partial class DecisionEngine
{
    [GeneratedRegex(@"\{ACCT_[0-9]+\}")]
    private static partial Regex TokenRx();

    public const string AskAccount =
        "Which patient account should I look up? Give me the account number, for example 'account 12345'. " +
        "I can't search by patient name. I'll show the claim lines and totals; say 'summary' for totals only.";

    // Words people type for reports that are not module keywords, so a slip in them still routes
    private static readonly string[] TypoWords =
    [
        "collect", "collected", "collections", "payments", "insurance", "patient", "patients", "denials", "denied", "claims", "payers",
        "practice", "practices", "provider", "providers", "receivable", "submissions", "resubmissions", "charges", "adjustments",
        "compare", "summary", "balance", "decrease", "decreased", "increase", "increased", "january", "february", "march", "april",
        "august", "september", "october", "november", "december", "quarter", "yesterday", "outstanding", "aging", "reasons", "versus",
    ];

    public static Decision Decide(
        Actor actor, string text, SessionSnapshot session, IReadOnlyList<IDomainModule> modules,
        ITokenizationService tokens, Guid conversationId, DateOnly? today = null, int maxTopN = 100)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var allowed = modules.Where(m => m.Allows(actor)).ToList();
        text = TypoFixer.Fix(text, modules.SelectMany(m => m.Keywords).Concat(TypoWords));
        var lower = text.ToLowerInvariant();
        var hasReportWords = allowed.Any(m => m.Keywords.Any(k => Coordinator.HasWord(lower, k)));
        var accounts = TokenRx().Matches(text).Select(m => m.Value).Distinct().ToList();
        // A message that names a report area or a patient account is a data question; "summary" in it is a style hint
        var intent = ConversationIntents.Classify(text, hasReportWords || accounts.Count > 0 || Coordinator.IsPatientLevel(text));
        var patientAllowed = ReportCatalog.Find(ReportCatalog.PatientBilling)!.AllowedRoles.Contains(actor.Role);

        // 1. Cancel and reset
        if (intent == MetaIntent.Cancel)
            return session.Pending is null
                ? new ReplyDecision("There's nothing pending to cancel. What would you like to look at?", TurnKind.Memory, "cancel: nothing pending")
                : new ReplyDecision("Okay, I've dropped that. What would you like to look at next?", TurnKind.Memory, "cancel pending",
                    PendingAction.Clear);
        if (intent == MetaIntent.ResetContext)
            return new ReplyDecision(
                "Okay, starting fresh: I've forgotten the patient and the report we were on. " +
                "Our earlier questions are still on record if you ask for a recap.",
                TurnKind.Memory, "reset context", PendingAction.Clear, ResetContext: true);

        // 2. Questions about the conversation itself. A side question never loses the open one.
        if (intent != MetaIntent.None)
        {
            // A replay can quote patient figures: only for a role that may still look up patient accounts
            if (!patientAllowed && intent is MetaIntent.SummarizeLast or MetaIntent.Repeat or MetaIntent.Recap
                && session.Turns.Any(t => t.Kind == TurnKind.Patient))
                return new ReplyDecision("Your role can't view patient account results, so I won't repeat them.",
                    TurnKind.Memory, $"memory: {intent} withheld (role)");
            var answer = intent switch
            {
                MetaIntent.FirstQuestion => MemoryAnswers.First(session),
                MetaIntent.LastQuestion => MemoryAnswers.Last(session),
                MetaIntent.Recap => MemoryAnswers.Recap(session, ConversationIntents.WantsBrief(text)),
                MetaIntent.SummarizeLast => MemoryAnswers.SummarizeLast(session),
                MetaIntent.Repeat => MemoryAnswers.Repeat(session),
                _ => MemoryAnswers.Capabilities(allowed, patientAllowed),
            };
            if (session.Pending is { } open) answer += $"\n\nStill open: {open.Ask}";
            return new ReplyDecision(answer, TurnKind.Memory, $"memory: {intent}");
        }

        // 3. An answer to a question we asked
        if (session.Pending is { } pending)
        {
            switch (pending.Kind)
            {
                case PendingKind.ConfirmAccount:
                    if (accounts.Count == 1) return Lookup(text, accounts[0], tokens, conversationId, "pending: new account given");
                    if (ConversationIntents.IsYes(text) && pending.Suggested is { } suggested)
                        return Lookup(text, suggested, tokens, conversationId, "pending: confirmed account in focus");
                    if (ConversationIntents.IsNo(text))
                        return Ask(AskAccount, PendingKind.PatientAccount, "pending: not the account in focus");
                    break; // something else: treat it as a new question

                case PendingKind.PatientAccount:
                    if (accounts.Count == 1) return Lookup(text, accounts[0], tokens, conversationId, "pending: account given");
                    if (accounts.Count > 1) return Ask(OneAtATime, PendingKind.PatientAccount, "pending: several accounts given");
                    // A short reply that is not a report question is most likely a name: say what is needed instead
                    if (!hasReportWords && !Coordinator.IsPatientLevel(text) && Words(text) <= 5)
                        return Ask("I can't look patients up by name or other details. Please give the account number, for example 'account 12345'.",
                            PendingKind.PatientAccount, "pending: reply was not an account number");
                    break;

                case PendingKind.ReportPeriod when pending.Suggested is { } original:
                    // The reply names a period: run the original question with it, as if it had been asked in one message
                    // A bare month ("May", "March") is only a month in context, so try it after "in" too
                    var periodReply = ClarifyPlanner.HasPeriod(text, day) ? text
                        : ClarifyPlanner.HasPeriod($"in {text}", day) && Words(text) <= 3 ? $"in {text}" : null;
                    if (periodReply is not null)
                    {
                        var joined = $"{original} {periodReply}";
                        var next = Decide(actor, joined, session with { Pending = null }, modules, tokens, conversationId, day, maxTopN);
                        // The model sees only the reply, so tell it the full question it is answering
                        return next is ModelDecision m
                            ? m with { Context = $"{m.Context} The user is answering your question about the period. Their full question is: \"{joined}\"".Trim() }
                            : next;
                    }
                    // A short reply with no period and no report words ("by payer", "ok") keeps the question open
                    if (!hasReportWords && Words(text) <= 4)
                        return new ReplyDecision(pending.Ask, TurnKind.Clarification, "pending: still no period",
                            PendingAction.Set, pending, Chips: AssistBuilder.PeriodChips);
                    // "by payer" answers the split but not the period: remember it and ask for the period again
                    if (Words(text) <= 4 && Regex.IsMatch(lower, @"(?<![a-z])by (payer|practice|month)s?(?![a-z])"))
                        return new ReplyDecision(pending.Ask, TurnKind.Clarification, "pending: split noted, no period",
                            PendingAction.Set, pending with { Suggested = $"{original} {text}" }, Chips: AssistBuilder.PeriodChips);
                    break; // a new question
            }
        }

        // 4. Patient account requests
        var plan = Coordinator.Plan(text, actor, modules);
        if (plan.Kind == PlanKind.Patient)
        {
            if (accounts.Count > 1) return Ask(OneAtATime, PendingKind.PatientAccount, "patient: several accounts");
            if (accounts.Count == 1) return Lookup(text, accounts[0], tokens, conversationId, "patient: account in the question");
            if (session.ActiveAccount is { } active)
            {
                if (ConversationIntents.ReferencesActiveAccount(text))
                    return Lookup(text, active, tokens, conversationId, "patient: refers to the account in focus");
                // Never silently reuse a patient: confirm first
                return new ReplyDecision(
                    $"Do you mean the account we just looked at ({active}), or a different one? Say yes, or give me the other account number.",
                    TurnKind.Clarification, "patient: confirm the account in focus",
                    PendingAction.Set, new PendingQuestion(PendingKind.ConfirmAccount,
                        $"Do you mean the account we just looked at ({active}), or a different one?", active));
            }
            return Ask(AskAccount, PendingKind.PatientAccount, "patient: account number missing");
        }

        // "show the details" while a patient is in focus
        if (plan.Kind is PlanKind.Open && !hasReportWords && session.ActiveAccount is { } inFocus
            && accounts.Count == 0 && ConversationIntents.AsksForDetail(text))
            return Lookup("details", inFocus, tokens, conversationId, "patient: details of the account in focus", forceDetail: true);

        // 4b. "drill into row 3": the month-by-month view of one payer, practice or provider from the last table
        if (DrillPlanner.Plan(text, session, maxTopN) is { } drill) return drill;

        // 4c. Only a period ("May"): continue the last report with it, or ask which report. Never left to the model,
        // which would guess the year and revive a question the user had dropped.
        if (ClarifyPlanner.PeriodOnly(text, hasReportWords, day) is { } phrase)
            return session.LastReport is not null
                ? Decide(actor, $"same for {phrase}", session, modules, tokens, conversationId, day, maxTopN)
                : ClarifyPlanner.AskReportFor(phrase);

        // 5-. Two reports could answer: ask which, with chips, before anything runs
        if (ClarifyPlanner.Ambiguous(text) is { } ambiguous) return ambiguous;

        // 5-. "What is a write-off?": the glossary answers in code. Questions about our own figures are not definitions.
        if (HealthcareKnowledge.Shared.Define(text) is { } term)
            return new ReplyDecision(HealthcareKnowledge.Explain(term), TurnKind.Memory, "glossary: " + term.Term);

        // 5-. "Why did collections drop?": a playbook the server runs, no model
        if (InvestigationPlanner.Plan(text, actor, day) is { } investigate) return investigate;

        // 5a. Business snapshot ("how are we doing"): the main reports, this period against the one before
        if (SnapshotPlanner.Plan(text, actor, day, maxTopN) is { } snapshot) return snapshot;

        // 5. Period comparison: two runs of one report, decided and run by the server
        if (ComparePlanner.Plan(text, plan, day, maxTopN) is { } compare) return compare;

        // 5b. No period named: ask for it (and offer a split) instead of guessing
        if (ClarifyPlanner.Plan(text, plan, session, day) is { } clarify) return clarify;

        // 6. Report questions
        return new ModelDecision(plan, ConversationIntents.WantsBrief(text),
            ContextFor(session, text, day), $"plan: {plan.Kind}");
    }

    private const string OneAtATime = "I can look up one patient account at a time. Which account should I start with?";

    private static ReplyDecision Ask(string question, PendingKind kind, string reason)
        => new(question, TurnKind.Clarification, reason, PendingAction.Set, new PendingQuestion(kind, question));

    private static Decision Lookup(
        string text, string token, ITokenizationService tokens, Guid conversationId, string reason, bool forceDetail = false)
        => tokens.TryResolveAccount(conversationId, token, out var account)
            ? new PatientDecision(token, account, !forceDetail && ConversationIntents.WantsSummaryOnly(text), reason)
            : Ask(AskAccount, PendingKind.PatientAccount, "patient: account token did not resolve");

    // Follow-ups: "same for April", "now by payer". Report name and period only, never figures.
    private static string? ContextFor(SessionSnapshot s, string text, DateOnly today)
    {
        var parts = new List<string>();
        if (s.LastReport is { } r)
            parts.Add($"Earlier in this conversation you ran \"{r.Name}\" for {r.Period}. If the new question leaves out the period, " +
                      "or says same, again, also, now, or asks for a different breakdown, reuse that period unless the user changes it.");
        // Named periods are turned into exact dates here so the model does not have to work them out
        var named = Periods.Find(text, today);
        if (named.Count > 0)
            parts.Add("The user's time words mean exactly: " +
                      string.Join("; ", named.Select(f => $"\"{f.Range.Phrase.Trim()}\" = {f.Range.Label}")) +
                      ". Use these dates; do not work out your own.");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static int Words(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
