using AiChatbot.Llm;

public record OpenAiInput(string? ApiKey, string? BaseUrl, string? Model);
public record OllamaInput(string? BaseUrl, string? Model);

// Who may use /api/settings. With Admin:ApiKey set, the X-Admin-Key header must match it.
// Without it, only loopback callers get in (the local dev UI and its proxy).
public static class SettingsAccess
{
    public static bool IsAllowed(System.Net.IPAddress? remote, string? headerKey, string? adminKey)
    {
        if (string.IsNullOrEmpty(adminKey)) return remote is not null && System.Net.IPAddress.IsLoopback(remote);
        if (string.IsNullOrEmpty(headerKey)) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(headerKey), System.Text.Encoding.UTF8.GetBytes(adminKey));
    }
}

// Provider URLs are server-side request targets: http(s) only, no embedded credentials,
// and never link-local or cloud-metadata addresses. Hostnames that resolve to such addresses are not checked.
public static class SettingsUrl
{
    public static bool IsAcceptable(string? s)
    {
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (u.UserInfo.Length > 0) return false;
        if (u.Host.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase)) return false;
        if (!System.Net.IPAddress.TryParse(u.IdnHost, out var ip)) return true;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.IsIPv6LinkLocal) return false;
        var b = ip.GetAddressBytes();
        return !(b.Length == 4 && b[0] == 169 && b[1] == 254) && !ip.Equals(System.Net.IPAddress.Parse("fd00:ec2::254"));
    }

    public static bool Same(string? a, string? b) =>
        string.Equals(a?.TrimEnd('/'), b?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}

// Body for PUT /api/settings and POST /api/settings/test.
// OpenAi.ApiKey absent or empty means "keep the saved key", but only while the OpenAI base URL is unchanged:
// a saved key is never sent to a different endpoint.
public record UpdateSettingsRequest(string? Provider, OpenAiInput? OpenAi, OllamaInput? Ollama)
{
    public Dictionary<string, string> Validate(AppSettings? saved = null)
    {
        var e = new Dictionary<string, string>();
        if (!ProviderNames.IsValid(Provider)) e["provider"] = "provider must be 'openai' or 'ollama'";
        if (!SettingsUrl.IsAcceptable(OpenAi?.BaseUrl)) e["openai.baseUrl"] = "A valid http(s) URL is required";
        else if (saved is not null && string.IsNullOrEmpty(OpenAi?.ApiKey) && !SettingsUrl.Same(OpenAi!.BaseUrl, saved.OpenAi.BaseUrl))
            e["openai.apiKey"] = "Enter the API key again when the base URL changes";
        if (string.IsNullOrWhiteSpace(OpenAi?.Model)) e["openai.model"] = "Model is required";
        if (!SettingsUrl.IsAcceptable(Ollama?.BaseUrl)) e["ollama.baseUrl"] = "A valid http(s) URL is required";
        if (Ollama?.Model is null) e["ollama.model"] = "Model is required (may be empty)";
        return e;
    }

    public AppSettings MergeWithSaved(AppSettings current) => new(
        Provider!,
        new OpenAiSettings(
            string.IsNullOrEmpty(OpenAi!.ApiKey)
                ? (SettingsUrl.Same(OpenAi.BaseUrl, current.OpenAi.BaseUrl) ? current.OpenAi.ApiKey : "")
                : OpenAi.ApiKey,
            OpenAi.BaseUrl!,
            OpenAi.Model!),
        new OllamaSettings(Ollama!.BaseUrl!, Ollama.Model!));
}

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/settings");

        var adminKey = app.Configuration["Admin:ApiKey"];
        g.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            return SettingsAccess.IsAllowed(http.Connection.RemoteIpAddress, http.Request.Headers["X-Admin-Key"], adminKey)
                ? await next(ctx)
                : Results.Json(new { error = "Settings are restricted" }, statusCode: StatusCodes.Status403Forbidden);
        });

        // Masked view: the raw key never appears in responses
        g.MapGet("/", (ISettingsRepository repo) => repo.GetMasked());

        g.MapPut("/", (UpdateSettingsRequest req, ISettingsRepository repo, ILlmProviderFactory factory) =>
        {
            var saved = repo.Get();
            var errors = req.Validate(saved);
            if (errors.Count > 0) return Results.BadRequest(new { errors });

            var settings = req.MergeWithSaved(saved);
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
            var saved = repo.Get();
            var errors = req.Validate(saved);
            if (errors.Count > 0) return Results.BadRequest(new { errors });

            var candidate = req.MergeWithSaved(saved);
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
