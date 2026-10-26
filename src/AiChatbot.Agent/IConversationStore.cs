using Microsoft.Extensions.AI;

namespace AiChatbot.Agent;

public interface IConversationStore
{
    IReadOnlyList<ChatMessage> GetHistory(Guid conversationId);
    void AddMessages(Guid conversationId, params ChatMessage[] messages);
}
