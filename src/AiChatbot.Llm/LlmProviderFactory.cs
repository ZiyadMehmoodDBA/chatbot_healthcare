using AiChatbot.Agent;
using AiChatbot.Safety;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Llm;

public interface ILlmProviderFactory
{
    ILlmProvider Create(string provider, AppSettings settings);
    void PreloadOllamaModel(OllamaSettings settings);
}

public sealed class LlmProviderFactory(IHttpClientFactory httpFactory, ILogger<LlmProviderFactory> log, IConfiguration config) : ILlmProviderFactory
{
    // Ollama is Local only on loopback or an operator-listed host (Safety:LocalHosts). The URL is editable through
    // the settings API, so anything else is treated as External and gets the full screen.
    private ProviderKind OllamaKind(OllamaSettings s)
    {
        if (!Uri.TryCreate(s.BaseUrl, UriKind.Absolute, out var uri)) return ProviderKind.External;
        var trusted = config.GetSection("Safety:LocalHosts").Get<string[]>() ?? [];
        return uri.IsLoopback || trusted.Contains(uri.Host, StringComparer.OrdinalIgnoreCase)
            ? ProviderKind.Local
            : ProviderKind.External;
    }

    public ILlmProvider Create(string provider, AppSettings settings) =>
        provider == ProviderNames.OpenAi
            ? new OpenAiProvider(settings.OpenAi)
            : new OllamaProvider(settings.Ollama, httpFactory.CreateClient(), OllamaKind(settings.Ollama));

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
