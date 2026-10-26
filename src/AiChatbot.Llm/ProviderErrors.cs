using System.ClientModel;
using System.Net;

namespace AiChatbot.Llm;

// Maps raw provider/network errors to short, user-facing messages
public static class ProviderErrors
{
    public static string Describe(Exception error)
    {
        if (error is OperationCanceledException or TimeoutException) return "Request timed out";

        var status = error switch
        {
            ClientResultException c => (int?)c.Status,
            HttpRequestException { StatusCode: { } s } => (int)s,
            _ => null,
        };
        if (status == 401) return "Invalid API key";
        if (status == 404) return "Model not found";

        // No status: host unreachable (connection refused / DNS)
        if (error is HttpRequestException { StatusCode: null }) return "Provider unreachable — is it running?";

        return string.IsNullOrWhiteSpace(error.Message) ? "Unknown error" : error.Message;
    }
}
