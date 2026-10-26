using AiChatbot.Agent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiChatbot.Llm;

public static class LlmServiceCollectionExtensions
{
    // The configured Llm:OpenAI:ApiKey (user-secrets / env) is the fallback when
    // settings.json has no key, mirroring the Node server's .env fallback.
    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration config, string settingsPath)
    {
        services.AddHttpClient();
        services.AddSingleton<ISettingsRepository>(
            new JsonSettingsRepository(settingsPath, config["Llm:OpenAI:ApiKey"] ?? ""));
        services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>();
        services.AddSingleton<IChatClientProvider, SettingsChatClientProvider>();
        return services;
    }
}
