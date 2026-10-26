using AiChatbot.Llm;

public record OpenAiInput(string? ApiKey, string? BaseUrl, string? Model);
public record OllamaInput(string? BaseUrl, string? Model);

// Body for PUT /api/settings and POST /api/settings/test.
// OpenAi.ApiKey absent or empty means "keep the saved key".
public record UpdateSettingsRequest(string? Provider, OpenAiInput? OpenAi, OllamaInput? Ollama)
{
    public Dictionary<string, string> Validate()
    {
        var e = new Dictionary<string, string>();
        if (!ProviderNames.IsValid(Provider)) e["provider"] = "provider must be 'openai' or 'ollama'";
        if (!IsHttpUrl(OpenAi?.BaseUrl)) e["openai.baseUrl"] = "A valid http(s) URL is required";
        if (string.IsNullOrWhiteSpace(OpenAi?.Model)) e["openai.model"] = "Model is required";
        if (!IsHttpUrl(Ollama?.BaseUrl)) e["ollama.baseUrl"] = "A valid http(s) URL is required";
        if (Ollama?.Model is null) e["ollama.model"] = "Model is required (may be empty)";
        return e;
    }

    private static bool IsHttpUrl(string? s) =>
        Uri.TryCreate(s, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    // An absent/empty apiKey keeps the key already saved on the server
    public AppSettings MergeWithSaved(AppSettings current) => new(
        Provider!,
        new OpenAiSettings(
            string.IsNullOrEmpty(OpenAi!.ApiKey) ? current.OpenAi.ApiKey : OpenAi.ApiKey,
            OpenAi.BaseUrl!,
            OpenAi.Model!),
        new OllamaSettings(Ollama!.BaseUrl!, Ollama.Model!));
}

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/settings");

        // Masked view: the raw key never appears in responses
        g.MapGet("/", (ISettingsRepository repo) => repo.GetMasked());

        g.MapPut("/", (UpdateSettingsRequest req, ISettingsRepository repo, ILlmProviderFactory factory) =>
        {
            var errors = req.Validate();
            if (errors.Count > 0) return Results.BadRequest(new { errors });

            var settings = req.MergeWithSaved(repo.Get());
            repo.Save(settings);

            if (settings.Provider == ProviderNames.Ollama && settings.Ollama.Model.Length > 0)
                factory.PreloadOllamaModel(settings.Ollama);

            return Results.Ok(repo.GetMasked());
        });

        // Live model discovery for the provider named in the query string
        g.MapGet("/models", async (string? provider, ISettingsRepository repo, ILlmProviderFactory factory, CancellationToken ct) =>
        {
            if (!ProviderNames.IsValid(provider))
                return Results.BadRequest(new { error = "provider must be 'openai' or 'ollama'" });
            try
            {
                var models = await factory.Create(provider!, repo.Get()).ListModelsAsync(ct);
                return Results.Ok(new { models });
            }
            catch (Exception ex)
            {
                // Upstream failure, not a client error
                return Results.Json(new { error = ProviderErrors.Describe(ex) }, statusCode: 502);
            }
        });

        // Tests a candidate config from the body WITHOUT saving it
        g.MapPost("/test", async (UpdateSettingsRequest req, ISettingsRepository repo, ILlmProviderFactory factory, CancellationToken ct) =>
        {
            var errors = req.Validate();
            if (errors.Count > 0) return Results.BadRequest(new { errors });

            var candidate = req.MergeWithSaved(repo.Get());
            try
            {
                return Results.Ok(await factory.Create(candidate.Provider, candidate).TestConnectionAsync(ct));
            }
            catch (Exception ex)
            {
                // e.g. no API key configured: still a graceful result
                return Results.Ok(new ConnectionResult(false, ProviderErrors.Describe(ex)));
            }
        });
    }
}
