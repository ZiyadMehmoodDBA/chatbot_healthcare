using System.Runtime.CompilerServices;
using System.Text;
using AiChatbot.Agent.Tools;
using AiChatbot.Safety;
using Microsoft.Extensions.AI;

namespace AiChatbot.Agent;

// The "Agent" box: owns history and talks to the LLM. Everything that goes in or out passes the safe-to-share layer.
public sealed class ChatAgent(IChatClientProvider llm, IConversationStore store, SafeToShareLayer safety, ReportTools tools)
{
    public const string NoInfoReply = "I don't have information about this context.";
    public const int MaxToolRounds = 4;

    // Scope guard. Sent with every request but never stored in history.
    public static readonly string SystemPrompt = $$"""
        You are a healthcare reporting assistant. You only help with healthcare reporting:
        clinical and operational reports, patient/case summaries, incident and adverse-event
        reports, quality and compliance metrics, and report structure or terminology.

        Rules:
        - If a request is outside healthcare reporting, reply with exactly: "{{NoInfoReply}}"
        - If you are not sure of the answer, or the question is vague, ambiguous, or lacks the
          context needed to answer reliably, reply with exactly: "{{NoInfoReply}}"
        - Never guess, invent figures, patient data, or facts. Do not answer from general
          knowledge when it is not clearly part of healthcare reporting.
        - Do not give medical diagnoses or treatment advice.
        - When you can answer, be concise and factual.

        Identifiers and data:
        - Patient account numbers appear as tokens like {ACCT_1}. Use a token exactly as written. Never guess or write an account number.
        - For patient or facility figures, call GetPatientSummary or GetFacilityMetrics. Never answer such questions from memory.
        - A tool result gives either values or placeholders such as {PATIENT_ADMISSIONS}. When it gives a placeholder, write that
          placeholder exactly and write no digits or number words for it; the server fills in the real figure.
        - If a tool returns an error, say you could not find that data.
        """;

    public string Model => llm.Resolve().Model;

    // Streams text deltas. History is saved only after the stream completes,
    // so a failed or cancelled request leaves the conversation untouched.
    // History holds tokenized text only: raw identifiers and server-filled figures are never stored.
    public async IAsyncEnumerable<string> StreamAsync(
        Guid conversationId,
        string prompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chat = llm.Resolve();
        var prepared = safety.Prepare(conversationId, prompt, chat.Kind);
        if (prepared.BlockedReply is { } blocked)
        {
            store.AddMessages(conversationId,
                new ChatMessage(ChatRole.User, prepared.Text), new ChatMessage(ChatRole.Assistant, blocked));
            yield return blocked;
            yield break;
        }

        var userMessage = new ChatMessage(ChatRole.User, prepared.Text);
        var messages = store.GetHistory(conversationId).Append(userMessage)
            .Prepend(new ChatMessage(ChatRole.System, SystemPrompt))
            .ToList();
        // Low temperature: factual, repeatable answers
        var withTools = new ChatOptions { Temperature = 0.2f, Tools = [.. tools.Declarations] };
        var withoutTools = new ChatOptions { Temperature = 0.2f };

        var live = chat.Kind == ProviderKind.Local;           // Local streams as it goes; External is validated first
        var values = new Dictionary<string, string>();        // every tool value, for server-side placeholder fill
        var shown = new List<string>();                       // values the model itself was allowed to see
        var stream = safety.StartStream(conversationId, values);
        var finalText = "";

        for (var round = 0; ; round++)
        {
            var calls = new List<FunctionCallContent>();
            var text = new StringBuilder();
            // The last round offers no tools, so the model has to answer
            var options = round < MaxToolRounds ? withTools : withoutTools;
            await foreach (var update in chat.Client.GetStreamingResponseAsync(messages, options, ct))
            {
                foreach (var c in update.Contents)
                    if (c is FunctionCallContent call) calls.Add(call);
                if (string.IsNullOrEmpty(update.Text)) continue;
                text.Append(update.Text);
                if (!live) continue;
                var piece = stream.Push(update.Text);
                if (piece.Length > 0) yield return piece;
            }
            if (live)
            {
                var rest = stream.Flush();
                if (rest.Length > 0) yield return rest;
            }

            if (calls.Count == 0) { finalText = text.ToString(); break; }

            messages.Add(new ChatMessage(ChatRole.Assistant, calls.Cast<AIContent>().ToList()));
            foreach (var call in calls)
            {
                var payload = RunTool(conversationId, call, chat.Kind, values, shown);
                messages.Add(new ChatMessage(ChatRole.Tool,
                    new List<AIContent> { new FunctionResultContent(call.CallId, payload) }));
            }
        }

        if (!live)
        {
            var output = ExternalOutput(conversationId, finalText, values, shown);
            if (output.Length > 0) yield return output;
        }

        store.AddMessages(conversationId, userMessage, new ChatMessage(ChatRole.Assistant, finalText));
    }

    // What the model gets back: real values when the policy allows, otherwise the placeholder names only.
    private Dictionary<string, string> RunTool(
        Guid conversationId, FunctionCallContent call, ProviderKind kind,
        Dictionary<string, string> values, List<string> shown)
    {
        var result = tools.Execute(conversationId, call.Name, call.Arguments);
        if (result.Error is { } error) return new() { ["error"] = error };

        foreach (var (key, value) in result.Values) values[key] = value;
        if (safety.Policy.Allows(kind, result.Class))
        {
            shown.AddRange(result.Values.Values);
            return new(result.Values);
        }

        var hidden = result.Values.Keys.ToDictionary(k => k, k => "{" + k + "}");
        hidden["note"] = "Values are inserted by the server. Write each placeholder exactly, with no digits.";
        return hidden;
    }

    // External text is checked before the user sees it: a number the model was never shown means it made one up,
    // so the answer is replaced by a server-built listing of the real tool values.
    private string ExternalOutput(Guid conversationId, string modelText, Dictionary<string, string> values, List<string> shown)
    {
        if (values.Count > 0 && ResponseFinalizer.HasInventedNumbers(modelText, shown))
        {
            var lines = values.Keys.Select(k => $"- {k.Replace('_', ' ').ToLowerInvariant()}: {{{k}}}");
            return safety.Finalize(conversationId, "Here is the data I found:\n" + string.Join("\n", lines), values);
        }
        return safety.Finalize(conversationId, modelText, values);
    }
}
