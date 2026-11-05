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
    bool ResetContext = false) : Decision(Reason);

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

    public static Decision Decide(
        Actor actor, string text, SessionSnapshot session, IReadOnlyList<IDomainModule> modules,
        ITokenizationService tokens, Guid conversationId, DateOnly? today = null, int maxTopN = 100)
    {
        var day = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var allowed = modules.Where(m => m.Allows(actor)).ToList();
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

        // 5. Period comparison: two runs of one report, decided and run by the server
        if (ComparePlanner.Plan(text, plan, day, maxTopN) is { } compare) return compare;

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
