using AiChatbot.Data;
using AiChatbot.Llm;

public record OpenAiInput(string? ApiKey, string? BaseUrl, string? Model);
public record OllamaInput(string? BaseUrl, string? Model);

// Who may use /api/settings. With Admin:ApiKey set, the X-Admin-Key header must match it.
// Without it, only loopback callers addressing the server as localhost get in (the local dev UI and its proxy);
// the Host check stops DNS-rebinding pages from reaching it through the browser.
public static class SettingsAccess
{
    private static readonly string[] LocalHostNames = ["localhost", "127.0.0.1", "[::1]", "::1"];

    public static bool IsAllowed(System.Net.IPAddress? remote, string? headerKey, string? adminKey, string? host = null)
    {
        if (string.IsNullOrEmpty(adminKey))
            return remote is not null && System.Net.IPAddress.IsLoopback(remote)
                && host is not null && LocalHostNames.Contains(HostOnly(host), StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(headerKey)) return false;
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(headerKey), System.Text.Encoding.UTF8.GetBytes(adminKey));
    }

    // "localhost:5070" -> "localhost", "[::1]:5070" -> "[::1]"
    private static string HostOnly(string host)
    {
        var h = host.Trim();
        if (h.StartsWith('[')) { var end = h.IndexOf(']'); return end < 0 ? h : h[..(end + 1)]; }
        var colon = h.LastIndexOf(':');
        return colon > 0 && h.IndexOf(':') == colon ? h[..colon] : h;
    }
}

// Provider URLs are server-side request targets: http(s) only, no embedded credentials, and never link-local or
// cloud-metadata addresses (including IPv4 hidden in IPv4-mapped, IPv4-compatible and NAT64 IPv6 forms).
// Hostnames are not resolved, so a name that points at such an address is not caught here.
public static class SettingsUrl
{
    private static readonly string[] MetadataNames = ["metadata", "metadata.google.internal", "instance-data"];
    private static readonly byte[] AwsImdsV6 = System.Net.IPAddress.Parse("fd00:ec2::254").GetAddressBytes();

    public static bool IsAcceptable(string? s)
    {
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
        if (u.UserInfo.Length > 0) return false;
        var host = u.IdnHost.TrimEnd('.').ToLowerInvariant();
        if (MetadataNames.Contains(host)) return false;
        if (!System.Net.IPAddress.TryParse(host, out var ip)) return true;
        // Zone IDs change IPAddress equality but not the target, so refuse them and compare raw bytes
        if (host.Contains('%') || (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && ip.ScopeId != 0)) return false;
        if (ip.IsIPv6LinkLocal || ip.GetAddressBytes().SequenceEqual(AwsImdsV6)) return false;
        var v4 = EmbeddedIPv4(ip);
        return v4 is null || !(v4[0] == 169 && v4[1] == 254);
    }

    // The IPv4 address carried by ::ffff:a.b.c.d, ::a.b.c.d or 64:ff9b::a.b.c.d (NAT64); null for other addresses
    private static byte[]? EmbeddedIPv4(System.Net.IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        if (b.Length == 4) return b;
        var zeroPrefix = b[..10].All(x => x == 0);
        var mapped = zeroPrefix && b[10] == 0xff && b[11] == 0xff;
        var compatible = zeroPrefix && b[10] == 0 && b[11] == 0;
        var nat64 = b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xff && b[3] == 0x9b && b[4..12].All(x => x == 0);
        return mapped || compatible || nat64 ? b[12..] : null;
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
            return SettingsAccess.IsAllowed(http.Connection.RemoteIpAddress, http.Request.Headers["X-Admin-Key"], adminKey, http.Request.Host.Value)
                ? await next(ctx)
                : Results.Json(new { error = "Settings are restricted" }, statusCode: StatusCodes.Status403Forbidden);
        });

        g.MapDatabaseSettings();

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
