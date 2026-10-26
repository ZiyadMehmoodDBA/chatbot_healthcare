using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiChatbot.Safety;

public static class SafetyServiceCollectionExtensions
{
    // Safety:AllowAggregateToExternal (default false) lets facility-level figures reach an external model.
    public static IServiceCollection AddSafety(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton(new PolicyOptions(config.GetValue("Safety:AllowAggregateToExternal", false)));
        services.AddSingleton<PolicyGate>();
        services.AddSingleton<ITokenizationService, TokenizationService>();
        services.AddSingleton<SafeToShareLayer>();
        return services;
    }
}
