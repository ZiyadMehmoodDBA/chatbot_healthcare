using System.Globalization;
using System.Text;
using AiChatbot.Agent.Domains;

namespace AiChatbot.Agent.Conversation;

// Answers to questions about the conversation, built from session memory. No model is involved, so they are exact,
// free, and work the same with any provider. Text may carry {ACCT_n} tokens; the agent fills them in for the user.
public static class MemoryAnswers
{
    public const int RecapLines = 12;

    public static string First(SessionSnapshot s)
        => s.Questions.FirstOrDefault() is { } t
            ? Describe("Your first question was", t)
            : "You haven't asked me anything yet in this conversation.";

    public static string Last(SessionSnapshot s)
        => s.Questions.LastOrDefault() is { } t
            ? Describe("Your previous question was", t)
            : "You haven't asked me anything yet in this conversation.";

    public static string Recap(SessionSnapshot s, bool brief)
    {
        var qs = s.Questions.ToList();
        if (qs.Count == 0) return "We haven't covered anything yet. Ask me about payments, A/R aging, claims, denials, or a patient account.";

        var sb = new StringBuilder();
        if (brief)
        {
            sb.Append($"So far, {Questions(qs.Count)}: ");
            sb.Append(string.Join("; ", qs.TakeLast(RecapLines).Select((t, i) => $"{qs.Count - Math.Min(qs.Count, RecapLines) + i + 1}) {Topic(t)}")));
            sb.Append('.');
            return sb.ToString();
        }

        sb.AppendLine($"Here is what we've covered so far ({Questions(qs.Count)}):");
        if (qs.Count > RecapLines) sb.AppendLine($"(The first {qs.Count - RecapLines} are not shown; your first question was “{Shorten(qs[0].Question, 80)}”.)");
        foreach (var t in qs.TakeLast(RecapLines))
            sb.AppendLine($"{t.Number}. “{Shorten(t.Question, 100)}” → {Outcome(t)}");
        return sb.ToString().TrimEnd();
    }

    // "summarize that": the short form of the last answer
    public static string SummarizeLast(SessionSnapshot s)
    {
        if (s.Questions.LastOrDefault() is not { } t)
            return "There's nothing to summarize yet. Ask me for a report first.";
        if (t.Reports.Count > 0)
            return "In short: " + string.Join(" ", t.Reports.Select(r => $"{r.Name}, {r.Headline}"));
        return t.Kind switch
        {
            TurnKind.Clarification => $"Nothing to summarize yet: I'm still waiting for your answer. {t.Answer}",
            TurnKind.Refused => $"There was no result to summarize. {t.Answer}",
            _ => "In short: " + Shorten(t.Answer, 240),
        };
    }

    public static string Repeat(SessionSnapshot s)
    {
        if (s.Questions.LastOrDefault() is not { } t) return "I haven't answered anything yet in this conversation.";
        var tail = t.Reports.Count > 0 ? "\n(The table is shown with that answer above.)" : "";
        return "Here it is again:\n" + t.Answer + tail;
    }

    // Example questions per area, shown with the help answer. Only areas the role can use are listed; no years (the
    // input screen blocks bare numbers) and no names. Add an example here when an area gets a new report.
    private static readonly Dictionary<string, string[]> Examples = new()
    {
        ["payments"] = ["How much came from insurance versus patients in May?", "Which payers pay us the most last month?", "Payments by practice last quarter", "Payments by month year to date"],
        ["accounts receivable"] = ["How much is outstanding in A/R?", "How old is our A/R?", "How much A/R is over 90 days?", "Which payers owe us the most?"],
        ["claims"] = ["How many claims did we process in May?", "Which providers bill the most last month?", "Claims by practice last quarter"],
        ["denials"] = ["How many claims were denied in April?", "Why are claims being denied?", "Denials by payer last quarter"],
        ["charges"] = ["Charges and adjustments by practice last month", "Charges by month year to date"],
        ["submissions"] = ["How many claims did we submit last week?", "How many claims were resubmitted in May?", "Top payment remark codes last month"],
    };

    public static string Capabilities(IReadOnlyList<IDomainModule> modules, bool patientLookup)
    {
        var sb = new StringBuilder("Here's what you can ask me. I answer billing and revenue cycle questions from the approved reports:\n");
        foreach (var m in modules)
        {
            sb.AppendLine($"- {Capitalize(m.Description)}");
            if (Examples.TryGetValue(m.Domain, out var ex))
                sb.AppendLine("  e.g. " + string.Join(" / ", ex.Select(e => $"\"{e}\"")));
        }
        sb.AppendLine("- Comparisons and the big picture: \"Compare payments this month vs last month\", \"How is the business doing?\"");
        sb.AppendLine("- Follow-ups: \"same by payer\", \"same for April\", \"drill into row 2\" (or the Month by month button)");
        if (patientLookup)
            sb.AppendLine("- One patient account's billing balance (give me an account number, like 'account 12345'; I can't search by name)");
        sb.AppendLine("Name a period when you ask (last month, May, Q1, year to date); if you don't, I'll ask. " +
                      "You can also ask about our conversation (\"what was my first question?\", \"summarize our conversation\", " +
                      "\"summarize that\") or say \"start over\".");
        sb.Append("Reports are drafts until the business owner approves them. Things I can't answer yet, like readmission rates or a collection rate, I'll tell you so rather than guess.");
        return sb.ToString();
    }

    private static string Describe(string prefix, TurnRecord t)
    {
        var when = t.AtUtc.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";
        return $"{prefix}: “{Shorten(t.Question, 200)}” (asked at {when}). {Outcome(t)}";
    }

    private static string Outcome(TurnRecord t) => t.Kind switch
    {
        TurnKind.Clarification => $"I asked you: {Shorten(t.Answer, 160)}",
        TurnKind.Refused => $"I couldn't help with that: {Shorten(t.Answer, 160)}",
        _ when t.Reports.Count > 0 => string.Join(" ", t.Reports.Select(r => $"{r.Name}, {r.Headline}")),
        _ => "My answer: " + Shorten(t.Answer, 200),
    };

    private static string Topic(TurnRecord t)
        => t.Reports.Count > 0
            ? string.Join(" and ", t.Reports.Select(r => r.Name.ToLowerInvariant() + (r.Period.Length > 0 ? $" ({r.Period})" : "")))
            : Shorten(t.Question, 60);

    private static string Questions(int n) => n == 1 ? "1 question" : $"{n} questions";

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    public static string Shorten(string s, int max)
    {
        var one = string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return one.Length <= max ? one : one[..(max - 1)].TrimEnd() + "…";
    }
}
