using AiChatbot.Safety;
using Microsoft.Extensions.AI;

namespace AiChatbot.Agent;

// Kind defaults to External: an unlabelled provider gets the strictest handling
public sealed record ResolvedChat(IChatClient Client, string Model, ProviderKind Kind = ProviderKind.External);

// Resolved per request so settings changes apply without a restart
public interface IChatClientProvider
{
    ResolvedChat Resolve();
}
