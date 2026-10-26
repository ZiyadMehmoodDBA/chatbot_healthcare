using System.Collections.Concurrent;
using Microsoft.Extensions.AI;

namespace AiChatbot.Agent;

// Process-local history; swap for a DB-backed store later
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<Guid, List<ChatMessage>> _conversations = new();

    public IReadOnlyList<ChatMessage> GetHistory(Guid conversationId)
    {
        if (!_conversations.TryGetValue(conversationId, out var messages))
            return [];
        lock (messages) return messages.ToArray();
    }

    public void AddMessages(Guid conversationId, params ChatMessage[] messages)
    {
        var list = _conversations.GetOrAdd(conversationId, _ => []);
        lock (list) list.AddRange(messages);
    }
}
