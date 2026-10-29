using System.Security.Cryptography;
using System.Text;
using AiChatbot.Reporting;

namespace AiChatbot.Agent;

// Per-request context the host supplies. Actor comes from the host's verified identity, never from the prompt.
// Reports collects the structured results of tool calls so the host can send them to the UI directly: the model
// never has to repeat a table, and exact values reach the screen from the report service, not from model text.
public sealed class ChatTurn(Actor? actor, string correlationId)
{
    public Actor? Actor { get; } = actor;
    public string CorrelationId { get; } = correlationId;
    public List<ReportResult> Reports { get; } = [];

    // One line per decision or tool call, for the structured trace the host logs under CorrelationId
    public List<TraceStep> Trace { get; } = [];
    public int ToolCalls { get; set; }
}

// Kind: plan | tool | llm. Detail never holds user text or result values: names, counts and outcomes only.
public sealed record TraceStep(string Kind, string Name, string Outcome, long Ms = 0, string? Detail = null);

// History and token vaults are keyed by conversation id. Mixing the user into that key means one user can never
// read or continue another user's conversation, even by guessing its id.
public static class ConversationScope
{
    public static Guid For(string userId, Guid conversationId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}\n{conversationId:N}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
