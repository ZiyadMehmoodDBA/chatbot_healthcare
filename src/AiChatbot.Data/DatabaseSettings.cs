using Microsoft.Data.SqlClient;

namespace AiChatbot.Data;

public static class DbMode
{
    public const string Synthetic = "synthetic";
    public const string Sql = "sql";
    public static bool IsValid(string? s) => s is Synthetic or Sql;
}

public static class DbAuth
{
    public const string Windows = "windows";
    public const string Sql = "sql";
    public static bool IsValid(string? s) => s is Windows or Sql;
}

// What the settings screen edits. Fields, not a raw connection string, so nothing can be smuggled in through
// extra keywords (for example a second server or an attached database file).
public sealed record DatabaseSettings(
    string Mode,
    string Server,
    string Database,
    string Auth,
    string User,
    string Password,
    bool Encrypt,
    bool TrustServerCertificate,
    string PatientSummaryView,
    string FacilityMetricsView)
{
    public static DatabaseSettings Default { get; } = new(
        DbMode.Synthetic, "", "", DbAuth.Windows, "", "", Encrypt: true, TrustServerCertificate: false,
        "reporting.vw_PatientSummary", "reporting.vw_FacilityMetrics");

    // Remote servers are reached over TCP only. Without a protocol the driver falls back to Named Pipes, which
    // fails with a confusing Win32 error (1326). LocalDB and local shared memory keep their own protocol.
    private static string TcpTarget(string server)
    {
        if (server.Contains(':') && server.IndexOf(':') <= 6 && !server.StartsWith("[")) return server; // tcp:/np:/lpc:
        if (server.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase)
            || server is "." or "(local)" || server.StartsWith(".\\")) return server;
        return "tcp:" + server;
    }

    // Read-only intent and a short connect timeout; the password only ever lives in this string, in memory.
    public string BuildConnectionString()
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = TcpTarget(Server.Trim()),
            InitialCatalog = Database.Trim(),
            ConnectTimeout = 5,
            ApplicationName = "AiChatbot",
            ApplicationIntent = ApplicationIntent.ReadOnly,
            Encrypt = Encrypt ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = TrustServerCertificate,
        };
        if (Auth == DbAuth.Windows)
        {
            b.IntegratedSecurity = true;
        }
        else
        {
            b.UserID = User.Trim();
            b.Password = Password;
        }
        return b.ConnectionString;
    }
}

// Client-safe view: the password never leaves the server
public sealed record MaskedDatabaseSettings(
    string Mode,
    string Server,
    string Database,
    string Auth,
    string User,
    bool PasswordConfigured,
    bool Encrypt,
    bool TrustServerCertificate,
    string PatientSummaryView,
    string FacilityMetricsView,
    bool Saved,
    string Active)
{
    public static MaskedDatabaseSettings From(DatabaseSettings s, bool saved, string active) => new(
        s.Mode, s.Server, s.Database, s.Auth, s.User, s.Password.Length > 0, s.Encrypt, s.TrustServerCertificate,
        s.PatientSummaryView, s.FacilityMetricsView, saved, active);
}
