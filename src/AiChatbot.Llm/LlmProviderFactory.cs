using AiChatbot.Agent;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Llm;

public interface ILlmProviderFactory
{
    ILlmProvider Create(string provider, AppSettings settings);
    void PreloadOllamaModel(OllamaSettings settings);
}

public sealed class LlmProviderFactory(IHttpClientFactory httpFactory, ILogger<LlmProviderFactory> log) : ILlmProviderFactory
{
    public ILlmProvider Create(string provider, AppSettings settings) =>
        provider == ProviderNames.OpenAi
            ? new OpenAiProvider(settings.OpenAi)
            : new OllamaProvider(settings.Ollama, httpFactory.CreateClient());

    // Fire-and-forget: loading a large model must not stall a settings save
    public void PreloadOllamaModel(OllamaSettings settings) =>
        _ = Task.Run(async () =>
        {
            try { await OllamaProvider.PreloadAsync(httpFactory.CreateClient(), settings); }
            catch (Exception ex) { log.LogWarning(ex, "Ollama model preload failed"); }
        });
}

// Builds the active provider from saved settings on every call
public sealed class SettingsChatClientProvider(ISettingsRepository settings, ILlmProviderFactory factory) : IChatClientProvider
{
    public ResolvedChat Resolve()
    {
        var s = settings.Get();
        var provider = factory.Create(s.Provider, s);
        return new ResolvedChat(provider.ChatClient, provider.Model, provider.Kind);
    }
}
