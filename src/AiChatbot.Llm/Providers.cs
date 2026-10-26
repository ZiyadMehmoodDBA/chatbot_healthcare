using System.ClientModel;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using AiChatbot.Safety;

namespace AiChatbot.Llm;

// Adapter contract: callers never know which vendor is behind it
public interface ILlmProvider
{
    string Model { get; }
    ProviderKind Kind { get; }
    IChatClient ChatClient { get; }
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);
    Task<ConnectionResult> TestConnectionAsync(CancellationToken ct = default);
}

public abstract class LlmProviderBase : ILlmProvider
{
    protected static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(10);

    public abstract string Model { get; }
    public abstract ProviderKind Kind { get; }
    public abstract IChatClient ChatClient { get; }
    public abstract Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default);

    // Listing models succeeds only when the provider is reachable (and the key valid)
    public async Task<ConnectionResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await ListModelsAsync(ct);
            return new ConnectionResult(true);
        }
        catch (Exception ex)
        {
            return new ConnectionResult(false, ProviderErrors.Describe(ex));
        }
    }
}

// baseUrl is configurable so OpenAI-compatible endpoints (proxies, local servers) also work
public sealed partial class OpenAiProvider : LlmProviderBase
{
    private readonly OpenAIClient _client;
    private readonly OpenAiSettings _settings;

    public OpenAiProvider(OpenAiSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("OpenAI API key is not configured");
        _settings = settings;
        _client = new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(settings.BaseUrl) });
    }

    public override string Model => _settings.Model;
    // Data leaves the network, even for OpenAI-compatible proxies
    public override ProviderKind Kind => ProviderKind.External;
    public override IChatClient ChatClient => _client.GetChatClient(_settings.Model).AsIChatClient();

    [GeneratedRegex(@"^(gpt-|o\d|chatgpt-)")]
    private static partial Regex ChatFamily();

    // Account models, filtered to chat-capable families
    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ListTimeout);
        var models = await _client.GetOpenAIModelClient().GetModelsAsync(cts.Token);
        return models.Value.Select(m => m.Id).Where(id => ChatFamily().IsMatch(id)).Order().ToList();
    }
}

public sealed class OllamaProvider : LlmProviderBase
{
    private readonly HttpClient _http;
    private readonly OllamaSettings _settings;

    public OllamaProvider(OllamaSettings settings, HttpClient http)
    {
        _settings = settings;
        _http = http;
        _http.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        // Slow local models legitimately generate for minutes
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    public override string Model => _settings.Model;
    // Assumes the Ollama host is on infrastructure we control
    public override ProviderKind Kind => ProviderKind.Local;
    public override IChatClient ChatClient => new OllamaApiClient(_http, _settings.Model);

    // Locally installed models via /api/tags
    public override async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ListTimeout);
        var models = await new OllamaApiClient(_http).ListLocalModelsAsync(cts.Token);
        return models.Select(m => m.Name).ToList();
    }

    // Warms a model into Ollama's memory (empty messages = documented preload trick). Best-effort.
    public static async Task PreloadAsync(HttpClient http, OllamaSettings settings, CancellationToken ct = default)
    {
        http.BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(60);
        using var res = await http.PostAsJsonAsync("api/chat", new { model = settings.Model, messages = Array.Empty<object>() }, ct);
        res.EnsureSuccessStatusCode();
    }
}
