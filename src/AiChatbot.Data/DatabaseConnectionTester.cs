using Microsoft.Data.SqlClient;

namespace AiChatbot.Data;

public sealed record ViewCheck(string Name, bool Ok, string? Error = null);

// Connected: the server accepted the login. Views are checked only when connected. Warnings do not fail the test.
public sealed record DatabaseTestResult(
    bool Connected, string? Error, IReadOnlyList<ViewCheck> Views, IReadOnlyList<string> Warnings)
{
    public bool Ok => Connected && Views.All(v => v.Ok);
}

// "Is it connected?": opens a connection with the candidate settings, checks that both reporting views are
// readable with the columns the app needs, and warns when the login can change data. Read-only; reports short,
// fixed messages and never the password or the raw driver text.
public static class DatabaseConnectionTester
{
    public const string PatientColumns = "AccountNumber, Admissions, LastVisitDate, OpenIncidents";
    public const string FacilityColumns = "Facility, ReadmissionRatePct, AvgLengthOfStayDays, IncidentCount, AsOfDate";

    public static async Task<DatabaseTestResult> TestAsync(DatabaseSettings s, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            await using var conn = new SqlConnection(s.BuildConnectionString());
            await conn.OpenAsync(cts.Token);

            var views = new List<ViewCheck>
            {
                await CheckViewAsync(conn, s.PatientSummaryView, PatientColumns, cts.Token),
                await CheckViewAsync(conn, s.FacilityMetricsView, FacilityColumns, cts.Token),
            };
            return new DatabaseTestResult(true, null, views, await WarningsAsync(conn, cts.Token));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new DatabaseTestResult(false, Describe(ex), [], []);
        }
    }

    private static async Task<ViewCheck> CheckViewAsync(SqlConnection conn, string view, string columns, CancellationToken ct)
    {
        try
        {
            // Names are validated by the caller (plain identifiers); TOP (0) reads no rows
            var name = SqlReportDataReader.ValidateViewName(view, "view");
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT TOP (0) {columns} FROM {name}";
            cmd.CommandTimeout = 10;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            return new ViewCheck(view, true);
        }
        catch (SqlException ex)
        {
            return new ViewCheck(view, false, ex.Number switch
            {
                208 => "View not found",
                207 => $"View is missing required columns ({columns})",
                229 or 230 or 262 => "This login has no permission to read the view",
                _ => $"Could not read the view (SQL error {ex.Number})",
            });
        }
        catch (InvalidOperationException)
        {
            return new ViewCheck(view, false, "Not a valid view name");
        }
    }

    private static async Task<IReadOnlyList<string>> WarningsAsync(SqlConnection conn, CancellationToken ct)
    {
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT ISNULL(IS_SRVROLEMEMBER('sysadmin'), 0), ISNULL(IS_MEMBER('db_owner'), 0), " +
                "ISNULL(IS_MEMBER('db_datawriter'), 0), ISNULL(IS_MEMBER('db_ddladmin'), 0)";
            cmd.CommandTimeout = 5;
            await using var r = await cmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct) && Enumerable.Range(0, 4).Any(i => Convert.ToInt32(r.GetValue(i)) == 1))
                return ["This login can change data (sysadmin, db_owner, db_datawriter or db_ddladmin). Use a read-only login limited to the two views."];
        }
        catch (SqlException)
        {
            // Role lookup is advisory only
        }
        return [];
    }

    public static string Describe(Exception ex) => ex switch
    {
        SqlException e when e.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
            => "The server certificate is not trusted. Install a trusted certificate, or (development only) turn on Trust server certificate.",
        SqlException { Number: 18456 } => "Login failed. Check the user name and password.",
        SqlException { Number: 4060 } => "The database could not be opened. Check the database name and that the login can access it.",
        SqlException { Number: -2 or 258 } => "The server did not respond in time. Check the server name, port and firewall.",
        SqlException { Number: 53 or -1 or 40 or 10060 or 10061 or 11001 }
            => "The server could not be reached. Check the server name, port and firewall.",
        SqlException e => $"Could not connect (SQL error {e.Number}).",
        OperationCanceledException => "The connection timed out.",
        ArgumentException or InvalidOperationException => "The connection settings are not valid.",
        _ => "Could not connect.",
    };
}
