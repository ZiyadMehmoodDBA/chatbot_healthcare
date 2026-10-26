using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Text.Json;
using AiChatbot.Agent;
using AiChatbot.Agent.Tools;
using AiChatbot.Data;
using AiChatbot.Llm;
using AiChatbot.Safety;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddSafety(builder.Configuration);
builder.Services.AddDataProtection().SetApplicationName("AiChatbot");
builder.Services.AddReportData(
    builder.Configuration, builder.Environment, Path.Combine(builder.Environment.ContentRootPath, "database-settings.json"));
builder.Services.AddSingleton<ReportTools>();
builder.Services.AddLlm(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, "settings.json"));
builder.Services.AddScoped<ChatAgent>();

var app = builder.Build();

app.MapSettingsEndpoints();

app.MapGet("/api/health", () => new { message = "Welcome to Chat Bot" });

app.MapPost("/api/chat", async (ChatRequest req, HttpContext ctx, ILogger<Program> log) =>
{
    var errors = req.Validate();
    if (errors.Count > 0) return Results.BadRequest(new { errors });

    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var started = Stopwatch.StartNew();
    var streaming = false;

    async Task Write(object line) =>
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(line, json) + "\n", ctx.RequestAborted);

    try
    {
        // Resolved here, not as a handler parameter, so validation errors
        // (400) are not masked by a missing-API-key failure (500)
        var agent = ctx.RequestServices.GetRequiredService<ChatAgent>();
        // RequestAborted fires on client Stop/disconnect and halts the upstream LLM call
        await foreach (var delta in agent.StreamAsync(req.ConversationId, req.Prompt!.Trim(), ctx.RequestAborted))
        {
            if (!streaming) { ctx.Response.ContentType = "application/x-ndjson"; streaming = true; }
            await Write(new { delta });
        }
        if (!streaming) ctx.Response.ContentType = "application/x-ndjson";
        await Write(new { done = true, model = agent.Model, durationMs = started.ElapsedMilliseconds });
    }
    catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
    {
        // client went away; nothing to send
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Chat request failed");
        if (streaming)
            await Write(new { error = ProviderErrors.Describe(ex) }); // status already sent: in-band
        else
            return Results.Json(new { error = "Failed to generate a response" }, statusCode: 500);
    }
    return Results.Empty;
});

app.Run();

public record ChatRequest(string? Prompt, Guid ConversationId)
{
    public Dictionary<string, string> Validate()
    {
        var e = new Dictionary<string, string>();
        var p = Prompt?.Trim();
        if (string.IsNullOrEmpty(p)) e["prompt"] = "Prompt is required";
        else if (p.Length > 1000) e["prompt"] = "Prompt is too long (max 1000 characters)";
        if (ConversationId == Guid.Empty) e["conversationId"] = "A valid conversationId is required";
        return e;
    }
}

public partial class Program;
