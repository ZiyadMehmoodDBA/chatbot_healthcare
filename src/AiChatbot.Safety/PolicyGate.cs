namespace AiChatbot.Safety;

public sealed record PolicyOptions(bool AllowAggregateToExternal = false);

// Single decision point for "may this class of data reach this provider?"
public sealed class PolicyGate(PolicyOptions options)
{
    public bool Allows(ProviderKind provider, DataClass data) => provider switch
    {
        ProviderKind.Local => true,
        _ => data switch
        {
            DataClass.Public => true,
            DataClass.Aggregate => options.AllowAggregateToExternal,
            _ => false, // patient-level data never leaves, no setting
        },
    };
}
