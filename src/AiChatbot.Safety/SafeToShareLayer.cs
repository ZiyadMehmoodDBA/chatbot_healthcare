namespace AiChatbot.Safety;

// BlockedReply set = the prompt must not be sent; Text is then the stored stand-in.
public sealed record PreparedPrompt(string Text, string? BlockedReply);

// Everything between the user and the LLM. One entry per direction so no caller can skip a step:
// Prepare (tokenize, screen) on the way in, Policy for tool data, Finalize on the way out.
public sealed class SafeToShareLayer(ITokenizationService tokens, PolicyGate policy)
{
    public PolicyGate Policy { get; } = policy;
    public ITokenizationService Tokens { get; } = tokens;

    public PreparedPrompt Prepare(Guid conversationId, string prompt, ProviderKind provider)
    {
        var t = Tokens.Tokenize(conversationId, prompt);
        if (provider == ProviderKind.External && !ExternalInputScreen.IsSafe(t.Text, t.KnownTokens))
            return new PreparedPrompt(ExternalInputScreen.Withheld, ExternalInputScreen.BlockedReply);
        return new PreparedPrompt(t.Text, null);
    }

    // Model text -> user text: server-supplied values for {NAMED} placeholders, real ids for {ACCT_n} tokens,
    // anything unresolved is replaced so a raw placeholder never reaches the user.
    public string Finalize(Guid conversationId, string modelText, IReadOnlyDictionary<string, string> values)
        => ResponseFinalizer.Fill(Tokens.Detokenize(conversationId, modelText), values);

    public StreamingFinalizer StartStream(Guid conversationId, IReadOnlyDictionary<string, string> values)
        => new(s => Finalize(conversationId, s, values));
}
