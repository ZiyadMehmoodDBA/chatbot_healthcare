using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiChatbot.Llm;

public interface ISettingsRepository
{
    AppSettings Get();
    void Save(AppSettings settings);
    MaskedSettings GetMasked();
}

// settings.json next to the app, gitignored. Missing/partial/corrupt file falls back to defaults.
public sealed class JsonSettingsRepository(string filePath, string defaultApiKey) : ISettingsRepository
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly object _lock = new();

    private AppSettings Defaults() => new(
        ProviderNames.OpenAi,
        new OpenAiSettings(defaultApiKey, "https://api.openai.com/v1", "gpt-4o-mini"),
        new OllamaSettings("http://localhost:11434", ""));

    // Lenient shape so partial files still load
    private sealed class Raw
    {
        public string? Provider { get; set; }
        [JsonPropertyName("openai")] public RawOpenAi? OpenAi { get; set; }
        public RawOllama? Ollama { get; set; }
    }
    private sealed class RawOpenAi { public string? ApiKey { get; set; } public string? BaseUrl { get; set; } public string? Model { get; set; } }
    private sealed class RawOllama { public string? BaseUrl { get; set; } public string? Model { get; set; } }

    public AppSettings Get()
    {
        var d = Defaults();
        lock (_lock)
        {
            if (!File.Exists(filePath)) return d;
            try
            {
                var raw = JsonSerializer.Deserialize<Raw>(File.ReadAllText(filePath), Json);
                if (raw is null) return d;
                // A saved key wins, but a saved EMPTY key falls back to the configured default
                var key = string.IsNullOrEmpty(raw.OpenAi?.ApiKey) ? d.OpenAi.ApiKey : raw.OpenAi!.ApiKey!;
                return new AppSettings(
                    raw.Provider == ProviderNames.Ollama ? ProviderNames.Ollama : ProviderNames.OpenAi,
                    new OpenAiSettings(key, raw.OpenAi?.BaseUrl ?? d.OpenAi.BaseUrl, raw.OpenAi?.Model ?? d.OpenAi.Model),
                    new OllamaSettings(raw.Ollama?.BaseUrl ?? d.Ollama.BaseUrl, raw.Ollama?.Model ?? d.Ollama.Model));
            }
            catch (JsonException)
            {
                return d; // corrupt JSON must not take the server down
            }
        }
    }

    // Atomic write: temp file then move, so a crash never leaves a torn file
    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            var temp = filePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json) + "\n");
            File.Move(temp, filePath, overwrite: true);
        }
    }

    public MaskedSettings GetMasked()
    {
        var s = Get();
        var key = s.OpenAi.ApiKey;
        return new MaskedSettings(
            s.Provider,
            new MaskedOpenAiSettings(
                key.Length > 0,
                key.Length > 0 ? "••••" + key[^Math.Min(4, key.Length)..] : "",
                s.OpenAi.BaseUrl,
                s.OpenAi.Model),
            s.Ollama);
    }
}
