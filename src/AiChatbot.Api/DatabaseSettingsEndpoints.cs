using AiChatbot.Data;

// Body for PUT /api/settings/database and POST /api/settings/database/test.
// Password absent or empty means "keep the saved password", but only while server, login name and auth mode are
// unchanged: a saved password is never sent to a different server or account.
public record DatabaseSettingsRequest(
    string? Mode, string? Server, string? Database, string? Auth, string? User, string? Password,
    bool? Encrypt, bool? TrustServerCertificate, string? PatientSummaryView, string? FacilityMetricsView)
{
    private const int MaxServer = 256;
    private const int MaxName = 128;

    public Dictionary<string, string> Validate(DatabaseSettings? saved, bool sqlAllowed)
    {
        var e = new Dictionary<string, string>();
        if (!DbMode.IsValid(Mode)) { e["mode"] = "mode must be 'synthetic' or 'sql'"; return e; }
        if (Mode == DbMode.Synthetic) return e;

        if (!sqlAllowed) e["mode"] = ReportingGuard.Refusal;

        if (!DbServer.IsAcceptable(Server)) e["server"] = "Enter a server name, for example myserver, myserver\\instance or myserver,1433";
        if (!IsPlainText(Database, MaxName)) e["database"] = "Database name is required";
        if (!DbAuth.IsValid(Auth)) e["auth"] = "auth must be 'windows' or 'sql'";
        else if (Auth == DbAuth.Sql)
        {
            if (!IsPlainText(User, MaxName)) e["user"] = "User name is required for SQL login";
            if (string.IsNullOrEmpty(Password) && !CanReuseSavedPassword(saved))
                e["password"] = "Enter the password again when the server or login changes";
        }
        if (!IsView(PatientSummaryView)) e["patientSummaryView"] = "Not a valid view name";
        if (!IsView(FacilityMetricsView)) e["facilityMetricsView"] = "Not a valid view name";
        return e;
    }

    public DatabaseSettings Merge(DatabaseSettings? saved)
    {
        var d = DatabaseSettings.Default;
        var auth = DbAuth.IsValid(Auth) ? Auth! : DbAuth.Windows;
        return new DatabaseSettings(
            Mode!,
            Server?.Trim() ?? "",
            Database?.Trim() ?? "",
            auth,
            auth == DbAuth.Sql ? User?.Trim() ?? "" : "",
            auth != DbAuth.Sql ? "" : !string.IsNullOrEmpty(Password) ? Password : CanReuseSavedPassword(saved) ? saved!.Password : "",
            Encrypt ?? d.Encrypt,
            TrustServerCertificate ?? d.TrustServerCertificate,
            string.IsNullOrWhiteSpace(PatientSummaryView) ? d.PatientSummaryView : PatientSummaryView.Trim(),
            string.IsNullOrWhiteSpace(FacilityMetricsView) ? d.FacilityMetricsView : FacilityMetricsView.Trim());
    }

    private bool CanReuseSavedPassword(DatabaseSettings? saved) =>
        saved is not null
        && saved.Auth == DbAuth.Sql && Auth == DbAuth.Sql
        && saved.Password.Length > 0
        && string.Equals(saved.Server.Trim(), Server?.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(saved.User.Trim(), User?.Trim(), StringComparison.OrdinalIgnoreCase);

    // Names go into a connection string field, so no separators or control characters
    private static bool IsPlainText(string? s, int max)
        => !string.IsNullOrWhiteSpace(s) && s.Trim().Length <= max && !s.Any(c => char.IsControl(c) || c is ';' or '=' or '{' or '}');

    private static bool IsView(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true; // default applies
        try { SqlReportDataReader.ValidateViewName(s.Trim(), "view"); return true; }
        catch (InvalidOperationException) { return false; }
    }
}

// The server field is a connection target, so it gets the same care as provider URLs: no link-local or
// cloud-metadata hosts. Accepts host, host\instance, host,port, tcp:host and (localdb)\name.
public static class DbServer
{
    public static bool IsAcceptable(string? server)
    {
        if (string.IsNullOrWhiteSpace(server) || server.Trim().Length > 256) return false;
        var s = server.Trim();
        if (s.Any(c => char.IsControl(c) || c is ';' or '=' or '{' or '}' or '"' or '\'' or '/' or ' ')) return false;
        if (s.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        var end = s.IndexOfAny([',', '\\']);
        var host = end < 0 ? s : s[..end];
        if (host.Length == 0) return false;
        if (host.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)
            || host is "." or "(local)" or "localhost") return true;
        return SettingsUrl.IsAcceptable("http://" + host + "/");
    }
}

public static class DatabaseSettingsEndpoints
{
    public static void MapDatabaseSettings(this RouteGroupBuilder g)
    {
        // Masked view: the password never appears in responses
        g.MapGet("/database", (IDatabaseSettingsRepository repo, ReportingOptions options, IReportingSource source) =>
        {
            var saved = repo.Get();
            var shown = saved ?? DatabaseSettings.Default with
            {
                Mode = options.Provider.Equals(ReportingOptions.Sql, StringComparison.OrdinalIgnoreCase) ? DbMode.Sql : DbMode.Synthetic,
                PatientSummaryView = options.PatientSummaryView,
                FacilityMetricsView = options.FacilityMetricsView,
            };
            return MaskedDatabaseSettings.From(shown, saved is not null, Describe(source, saved));
        });

        g.MapPut("/database", (DatabaseSettingsRequest req, IDatabaseSettingsRepository repo,
            ReportingOptions options, IReportingSource source, Microsoft.Extensions.Hosting.IHostEnvironment env) =>
        {
            var saved = repo.Get();
            var errors = req.Validate(saved, ReportingGuard.SqlAllowed(env, options));
            if (errors.Count > 0) return Results.BadRequest(new { errors });

            var settings = req.Merge(saved);
            repo.Save(settings);
            return Results.Ok(MaskedDatabaseSettings.From(settings, true, Describe(source, settings)));
        });

        // Tests a candidate from the body WITHOUT saving it
        g.MapPost("/database/test", async (DatabaseSettingsRequest req, IDatabaseSettingsRepository repo,
            ReportingOptions options, Microsoft.Extensions.Hosting.IHostEnvironment env, CancellationToken ct) =>
        {
            if (req.Mode != DbMode.Sql)
                return Results.BadRequest(new { errors = new Dictionary<string, string> { ["mode"] = "Choose SQL Server to test a connection" } });

            var saved = repo.Get();
            var errors = req.Validate(saved, ReportingGuard.SqlAllowed(env, options));
            if (errors.Count > 0) return Results.BadRequest(new { errors });

            return Results.Ok(await DatabaseConnectionTester.TestAsync(req.Merge(saved), ct));
        });
    }

    private static string Describe(IReportingSource source, DatabaseSettings? saved)
    {
        try
        {
            if (!source.Current().UseSql) return "Demo data (synthetic)";
            return saved is { Mode: DbMode.Sql } ? $"SQL Server: {saved.Server} / {saved.Database}" : "SQL Server (from configuration)";
        }
        catch (InvalidOperationException)
        {
            return "SQL Server is blocked: " + ReportingGuard.Refusal;
        }
    }
}
