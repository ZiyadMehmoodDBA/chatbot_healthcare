using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Text.Json;
using AiChatbot.Agent;
using AiChatbot.Agent.Tools;
using AiChatbot.Data;
using AiChatbot.Llm;
using AiChatbot.Reporting;
using AiChatbot.Safety;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IConversationStore, InMemoryConversationStore>();
builder.Services.AddSafety(builder.Configuration);
builder.Services.AddDataProtection().SetApplicationName("AiChatbot");
builder.Services.AddReportData(
    builder.Configuration, builder.Environment, Path.Combine(builder.Environment.ContentRootPath, "database-settings.json"));
builder.Services.AddSingleton<ReportTools>();
builder.Services.AddSingleton<RcmReportTools>();
builder.Services.AddReporting(builder.Configuration,
    sp => ReportEndpoints.ConnectionFor(builder.Configuration, sp.GetRequiredService<IReportingSource>()),
    // Durable audit needs its own INSERT-only login; without ConnectionStrings:RcmAudit the log is the only record
    sp => builder.Configuration.GetConnectionString("RcmAudit") is { Length: > 0 } cs
        ? () => new Microsoft.Data.SqlClient.SqlConnection(cs) : null);
builder.Services.AddHostedService<AuditWriterService>();
builder.Services.AddHealthChecks().AddCheck<AuditHealthCheck>("audit");
var authMode = builder.AddRcmAuth();
builder.Services.AddLlm(builder.Configuration, Path.Combine(builder.Environment.ContentRootPath, "settings.json"));
builder.Services.AddScoped<ChatAgent>();

var app = builder.Build();

app.UseRcmAuth(authMode);

app.MapSettingsEndpoints();
app.MapReportEndpoints(authMode);

app.MapGet("/api/health", () => new { message = "Welcome to Chat Bot" });
// Status word only, no details. Not part of /api/health so operators choose whether audit trouble pulls the service out.
app.MapHealthChecks("/api/health/audit", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResultStatusCodes =
    {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = 200,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = 200,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = 503,
    },
});

var chatEndpoint = app.MapPost("/api/chat", async (ChatRequest req, HttpContext ctx, ILogger<Program> log) =>
{
    var errors = req.Validate();
    if (errors.Count > 0) return Results.BadRequest(new { errors });

    // Chat is RCM mode: a signed-in actor is required, and every report runs with that actor's scope
    var actor = ctx.RequestServices.GetRequiredService<IActorProvider>().Current;
    if (ActorGate.Deny(ctx, actor, ctx.RequestServices.GetRequiredService<IReportAudit>(), "chat", "chat") is { } denied) return denied;
    actor = actor!;
    var turn = new ChatTurn(actor, ctx.TraceIdentifier);

    var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var started = Stopwatch.StartNew();
    var streaming = false;
    string? modelName = null;

    async Task Write(object line) =>
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(line, json) + "\n", ctx.RequestAborted);

    // Report tables go to the browser straight from the report service, as their own stream lines
    var reportsSent = 0;
    async Task WriteReports()
    {
        while (reportsSent < turn.Reports.Count)
        {
            if (!streaming) { ctx.Response.ContentType = "application/x-ndjson"; streaming = true; }
            await Write(new { report = turn.Reports[reportsSent++] });
        }
    }

    try
    {
        // Resolved here, not as a handler parameter, so validation errors
        // (400) are not masked by a missing-API-key failure (500)
        var agent = ctx.RequestServices.GetRequiredService<ChatAgent>();
        modelName = agent.Model;
        // RequestAborted fires on client Stop/disconnect and halts the upstream LLM call
        await foreach (var delta in agent.StreamAsync(req.ConversationId, req.Prompt!.Trim(), ctx.RequestAborted, turn))
        {
            await WriteReports();
            if (!streaming) { ctx.Response.ContentType = "application/x-ndjson"; streaming = true; }
            await Write(new { delta });
        }
        await WriteReports();
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
    finally
    {
        // One structured line per question: who, what the coordinator decided, each tool call, how long. It joins the
        // report audit lines by correlation id. Prompt text and result values are never logged.
        log.LogInformation(
            "ChatTrace corr={CorrelationId} user={UserId} role={Role} model={Model} ms={Ms} reports={Reports} steps={Steps}",
            turn.CorrelationId, actor.UserId, actor.Role, modelName, started.ElapsedMilliseconds, turn.Reports.Count,
            string.Join(" | ", turn.Trace.Select(t => $"{t.Kind}:{t.Name}:{t.Outcome}:{t.Ms}ms" + (t.Detail is null ? "" : $"({t.Detail})"))));
    }
    return Results.Empty;
});
if (authMode == AuthSetup.ModeAzureAd) chatEndpoint.RequireAuthorization(AuthSetup.Policy);

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

// Drains the audit queue into SQL in the background; stops cleanly on shutdown
public sealed class AuditWriterService(IReportAudit audit) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => audit is DurableReportAudit d ? d.RunAsync(stoppingToken) : Task.CompletedTask;
}

// Healthy: audit writes work. Degraded: some entries were lost earlier. Unhealthy: the start-up probe or the latest write failed.
// Log-only mode (no ConnectionStrings:RcmAudit) is reported as degraded so it is visible, never silent.
public sealed class AuditHealthCheck(IReportAudit audit) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken ct = default)
    {
        if (audit is not DurableReportAudit d)
            return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded("audit is log-only"));
        var h = d.Health;
        return Task.FromResult(h.State switch
        {
            AuditHealthState.Healthy => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("audit ok"),
            AuditHealthState.Degraded => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Degraded("audit entries were lost earlier"),
            _ => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("audit writes are failing"),
        });
    }
}
