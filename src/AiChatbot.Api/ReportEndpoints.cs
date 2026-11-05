using System.Data.Common;
using AiChatbot.Data;
using AiChatbot.Reporting;
using Microsoft.Data.SqlClient;

// Who is calling. Development only: a fixed identity from configuration (Auth:DevActor). It is never read from a
// header or the request body. Outside Development there is no provider until real sign-in (OIDC) is wired, so
// every report call is refused with 401.
public sealed class DevActorOptions
{
    public string? UserId { get; set; }
    public string? Role { get; set; }
    public long[] Practices { get; set; } = [];
}

public sealed class ConfiguredActorProvider(Actor? actor) : IActorProvider
{
    public Actor? Current { get; } = actor;

    public static Actor? FromConfig(IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsDevelopment()) return null;
        var o = config.GetSection("Auth:DevActor").Get<DevActorOptions>();
        if (o is null || string.IsNullOrWhiteSpace(o.UserId) || string.IsNullOrWhiteSpace(o.Role)) return null;
        return new Actor(o.UserId, o.Role, o.Practices);
    }
}

public sealed record ReportRunBody(
    DateOnly? StartDate, DateOnly? EndDate, DateOnly? AsOfDate, string? DateBasis,
    long[]? PracticeCodes, long? PayerId, long? ProviderId, int? TopN)
{
    public ReportRequest ToRequest()
        => new(StartDate, EndDate, AsOfDate, DateBasis, PracticeCodes, PayerId, ProviderId, TopN);
}

public static class ReportEndpoints
{
    // ConnectionStrings:RcmReports (a read-only login) wins, so the report API does not depend on the demo reader's
    // Reports:Provider. Otherwise the active source decides; the synthetic demo has no database to report on.
    public static Func<DbConnection> ConnectionFor(IConfiguration config, IReportingSource source) => () =>
    {
        var direct = config.GetConnectionString("RcmReports");
        if (!string.IsNullOrWhiteSpace(direct)) return new SqlConnection(direct);
        var cfg = source.Current();
        if (!cfg.UseSql) throw new ReportException(ReportErrorKind.Unavailable, "No reporting database is configured");
        return new SqlConnection(cfg.ConnectionString);
    };

    public static void MapReportEndpoints(this WebApplication app, string authMode = AuthSetup.ModeDev)
    {
        var g = app.MapGroup("/api/reports");
        if (authMode == AuthSetup.ModeAzureAd) g.RequireAuthorization(AuthSetup.Policy);

        g.MapGet("/", (HttpContext ctx, IActorProvider who, ReportBroker broker, IReportAudit audit) =>
            ActorGate.Deny(ctx, who.Current, audit, "catalog", "list") ?? Results.Ok(broker.CatalogFor(who.Current!)));

        // Patient-level reports exist only in the chat flow: no generic run, no CSV export of one patient's lines
        static bool ChatOnly(string id) => ReportCatalog.Find(id)?.PatientLevel == true;

        g.MapPost("/{id}/run", async (string id, ReportRunBody body, HttpContext ctx, IActorProvider who, ReportBroker broker, IReportAudit audit)
            => ActorGate.Deny(ctx, who.Current, audit, ReportCatalog.AuditId(id), "run")
               ?? (ChatOnly(id) ? Results.Json(new { error = "Unknown report" }, statusCode: 404) : null)
               ?? await Handle(async () => Results.Ok(
                await broker.RunAsync(who.Current, id, body.ToRequest(), "run", ctx.TraceIdentifier, ctx.RequestAborted))));

        g.MapPost("/{id}/export", async (string id, ReportRunBody body, HttpContext ctx, IActorProvider who, ReportBroker broker, IReportAudit audit)
            => ActorGate.Deny(ctx, who.Current, audit, ReportCatalog.AuditId(id), "export")
               ?? (ChatOnly(id) ? Results.Json(new { error = "Unknown report" }, statusCode: 404) : null)
               ?? await Handle(async () =>
            {
                // Export re-runs the report so authorization and audit are applied again at download time
                var result = await broker.RunAsync(who.Current, id, body.ToRequest(), "export", ctx.TraceIdentifier, ctx.RequestAborted);
                return Results.File(
                    System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(CsvExport.ToCsv(result))).ToArray(),
                    "text/csv", $"{result.Evidence.ReportId}_{DateTime.UtcNow:yyyyMMdd}.csv");
            }));
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> run)
    {
        try { return await run(); }
        catch (ReportException ex)
        {
            var status = ex.Kind switch
            {
                ReportErrorKind.Unauthenticated => 401,
                ReportErrorKind.Forbidden => 403,
                ReportErrorKind.NotFound => 404,
                ReportErrorKind.Invalid => 400,
                _ => 503,
            };
            return Results.Json(new { error = ex.Message }, statusCode: status);
        }
    }
}
