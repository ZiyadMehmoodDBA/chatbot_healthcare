using System.Text.Json.Serialization;

namespace AiChatbot.Llm;

public static class ProviderNames
{
    public const string OpenAi = "openai";
    public const string Ollama = "ollama";

    public static bool IsValid(string? name) => name is OpenAi or Ollama;
}

// Full settings as stored on disk, includes the raw API key
public sealed record OpenAiSettings(string ApiKey, string BaseUrl, string Model);
public sealed record OllamaSettings(string BaseUrl, string Model);
public sealed record AppSettings(
    string Provider,
    [property: JsonPropertyName("openai")] OpenAiSettings OpenAi,
    OllamaSettings Ollama);

// Client-safe view: the raw key is replaced by a flag and a masked display value
public sealed record MaskedOpenAiSettings(bool ApiKeyConfigured, string ApiKeyDisplay, string BaseUrl, string Model);
public sealed record MaskedSettings(
    string Provider,
    [property: JsonPropertyName("openai")] MaskedOpenAiSettings OpenAi,
    OllamaSettings Ollama);

public sealed record ConnectionResult(
    bool Ok,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null);
