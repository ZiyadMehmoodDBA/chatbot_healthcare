using System.Text.RegularExpressions;

namespace AiChatbot.Agent.Conversation;

// Questions about the conversation itself, answered from session memory without a model call.
public enum MetaIntent
{
    None,
    FirstQuestion,
    LastQuestion,
    // "summarize our conversation", "what did we discuss", "previous conversation"
    Recap,
    // "summarize that", "in short"
    SummarizeLast,
    // "say that again", "what was your last answer"
    Repeat,
    // "start over", "new topic", "forget that patient"
    ResetContext,
    // "never mind", "cancel" (only meaningful while a question is pending)
    Cancel,
    // "what can you do", "help"
    Capabilities,
}

// Deterministic phrase rules, on purpose: free, identical every time, easy to test, and they work with any model.
// They are matched against the user's own words, so a report question that merely contains "last" or "summary" must
// not match: the patterns need the conversation words (question, conversation, again, ...) or a very short message.
public static partial class ConversationIntents
{
    [GeneratedRegex(@"^\s*(?:please\s+)?(?:cancel|never\s*mind|nevermind|nvm|forget\s+it|skip\s+it|skip|stop)\s*[.!]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CancelRx();

    [GeneratedRegex(@"\b(start\s+over|start\s+again|start\s+afresh|new\s+topic|different\s+topic|clear\s+(?:the\s+|my\s+)?(?:context|chat|session)|reset(?:\s+(?:the\s+)?(?:chat|context|session))?|forget\s+(?:that|this|it|everything|the\s+(?:patient|account)|about\s+(?:that|this|the)\s+(?:patient|account)))\b", RegexOptions.IgnoreCase)]
    private static partial Regex ResetRx();

    [GeneratedRegex(@"\b(?:first|1st|initial|earliest|original|opening)\s+(?:question|message|query|request|thing)\b|\bwhat\s+did\s+i\s+(?:ask|say|type)\s+(?:first|at\s+first|at\s+the\s+(?:start|beginning)|in\s+the\s+beginning|to\s+begin\s+with)\b|\bwhat\s+did\s+we\s+start\s+with\b|\bhow\s+did\s+(?:we|this)\s+(?:start|begin)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FirstRx();

    [GeneratedRegex(@"\b(?:last|previous|prior|preceding|latest|most\s+recent)\s+(?:question|message|query|request)\b|\bwhat\s+did\s+i\s+(?:just\s+)?(?:ask|say|type)\b|\bwhat\s+was\s+i\s+(?:just\s+)?asking\b|\bmy\s+(?:last|previous)\s+one\b", RegexOptions.IgnoreCase)]
    private static partial Regex LastRx();

    [GeneratedRegex(@"\b(?:summari[sz]e|summary|recap|overview|review|go\s+over|sum\s+up)\b.{0,50}\b(?:conversation|chat|session|discussion|so\s+far|everything|we\s+(?:discussed|talked|covered|did|looked))\b|\bwhat\s+(?:have|did)\s+we\s+(?:discuss(?:ed)?|talk(?:ed)?|cover(?:ed)?|done|do|looked)\b|\b(?:previous|last|earlier|prior|past)\s+(?:conversation|chat|session|discussion)\b|\bconversation\s+(?:history|so\s+far|summary)\b|\b(?:list|show|give\s+me)\s+(?:all\s+)?(?:of\s+)?(?:my|the)\s+(?:questions|messages|queries)\b|\bwhat\s+(?:have\s+i|did\s+i)\s+(?:asked?|been\s+asking)\s+(?:so\s+far|today)\b|\bhow\s+many\s+questions\b", RegexOptions.IgnoreCase)]
    private static partial Regex RecapRx();

    [GeneratedRegex(@"\b(?:repeat|say\s+(?:that\s+)?again|show\s+(?:that\s+)?again|once\s+more|what\s+was\s+(?:your|the)\s+(?:last\s+|previous\s+)?(?:answer|reply|response)|what\s+did\s+you\s+(?:just\s+)?(?:say|answer|reply)|your\s+(?:last|previous)\s+(?:answer|reply|response))\b", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatRx();

    [GeneratedRegex(@"^\s*(?:please\s+)?(?:(?:can|could)\s+you\s+)?(?:summari[sz]e|sum\s+up|shorten|condense)(?:\s+(?:that|this|it|the\s+(?:result|results|answer|report|data|numbers?|table)))?(?:\s+please)?\s*[.?!]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SummarizeExactRx();

    [GeneratedRegex(@"\b(?:in\s+short|short\s+version|shorter|tl;?dr|in\s+a\s+nutshell|in\s+brief|briefly|one\s+line|one\s+sentence|quick\s+summary|just\s+the\s+summary|key\s+(?:points|takeaways?)|bottom\s+line|summary\s+please|give\s+me\s+(?:a|the)\s+summary|what\s+does\s+(?:that|it)\s+mean)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BriefRx();

    [GeneratedRegex(@"^\s*(?:help|help\s+me)\s*[.?!]?\s*$|\bwhat\s+can\s+you\s+(?:do|help)\b|\bwhat\s+do\s+you\s+do\b|\bhow\s+can\s+you\s+help\b|\bwhat\s+(?:kinds?\s+of\s+)?(?:reports|questions)\s+(?:i\s+)?(?:can|could|should|may)\s+(?:i\s+)?ask\b|\bwhat\s+(?:can|could|should|may)\s+i\s+ask\b|\bwhat\s+do\s+you\s+know\b|\bexamples?\s+of\s+(?:questions|what)\b|\bwhat\s+(?:reports|questions)\s+(?:can|do)\s+(?:you|i)\b|\byour\s+capabilities\b", RegexOptions.IgnoreCase)]
    private static partial Regex CapabilitiesRx();

    [GeneratedRegex(@"^\s*#?\s*([0-9]{4,12})\s*$")]
    private static partial Regex BareAccountRx();

    [GeneratedRegex(@"^\s*(?:yes|yeah|yep|yup|y|sure|ok|okay|correct|right|same|same\s+(?:one|patient|account)|that\s+one|that\s+(?:patient|account)|the\s+same(?:\s+(?:one|patient|account))?)\s*[.!]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex YesRx();

    [GeneratedRegex(@"^\s*(?:no|nope|nah|n|different|another|another\s+one|other|a\s+different\s+(?:one|account|patient)|not\s+(?:that|the\s+same)(?:\s+one)?)\s*[.!]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NoRx();

    // "that patient", "the same account", "their balance": points at the account already in focus
    [GeneratedRegex(@"\b(?:same|that|this|current|previous)\s+(?:patient|account|one)\b|\b(?:their|his|her)\s+(?:balance|statement|account|bill|charges)\b|\bthe\s+same\b|\bsame\s+(?:patient|account)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReferencesActiveRx();

    // "show the details" while a patient is in focus
    [GeneratedRegex(@"\b(?:details?|line\s+details?|breakdown|full\s+(?:statement|detail|list)|each\s+line|every\s+line|all\s+(?:the\s+)?lines)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DetailRx();

    // Totals only, no line list
    [GeneratedRegex(@"\b(?:balance\s+only|just\s+the\s+balance|only\s+the\s+balance|totals?\s+only|just\s+the\s+total|summary|summari[sz]e|how\s+much)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryOnlyRx();

    // hasReportWords: the question also names a report area, so a brief word is a style hint, not a request to
    // summarize the previous answer
    public static MetaIntent Classify(string text, bool hasReportWords)
    {
        if (CancelRx().IsMatch(text)) return MetaIntent.Cancel;
        if (ResetRx().IsMatch(text)) return MetaIntent.ResetContext;
        if (RecapRx().IsMatch(text)) return MetaIntent.Recap;
        if (FirstRx().IsMatch(text)) return MetaIntent.FirstQuestion;
        if (LastRx().IsMatch(text)) return MetaIntent.LastQuestion;
        if (RepeatRx().IsMatch(text)) return MetaIntent.Repeat;
        if (SummarizeExactRx().IsMatch(text)) return MetaIntent.SummarizeLast;
        if (!hasReportWords && WordCount(text) <= 8 && BriefRx().IsMatch(text)) return MetaIntent.SummarizeLast;
        if (CapabilitiesRx().IsMatch(text)) return MetaIntent.Capabilities;
        return MetaIntent.None;
    }

    // A report question asked in a short style ("payments by payer last month, briefly")
    public static bool WantsBrief(string text) => BriefRx().IsMatch(text);

    // Digits alone, as an answer to "which account?". The tokenizer only recognises labelled numbers.
    public static string? BareAccount(string text)
        => BareAccountRx().Match(text) is { Success: true } m ? m.Groups[1].Value : null;

    public static bool IsYes(string text) => YesRx().IsMatch(text);
    public static bool IsNo(string text) => NoRx().IsMatch(text);
    public static bool ReferencesActiveAccount(string text) => ReferencesActiveRx().IsMatch(text);
    public static bool AsksForDetail(string text) => DetailRx().IsMatch(text) && WordCount(text) <= 8;
    public static bool WantsSummaryOnly(string text) => SummaryOnlyRx().IsMatch(text);

    private static int WordCount(string s) => s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
