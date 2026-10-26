using AiChatbot.Agent.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;

namespace AiChatbot.Data;

// Where report data comes from right now. UseSql=false means the synthetic demo reader.
public sealed record ReportingConfig(
    bool UseSql, string ConnectionString, string PatientSummaryView, string FacilityMetricsView, int CommandTimeoutSeconds)
{
    public static ReportingConfig Synthetic { get; } = new(false, "", "", "", 10);
}

public interface IReportingSource
{
    // Throws InvalidOperationException when the active settings must not be used (for example SQL outside
    // Development without the no-auth acknowledgement)
    ReportingConfig Current();
}

public static class ReportingGuard
{
    public const string Refusal =
        "The SQL provider is refused outside Development: the API has no user authentication. " +
        "Add authentication, or set Reports:AcknowledgeNoUserAuth=true to accept the risk.";

    public static bool SqlAllowed(IHostEnvironment env, ReportingOptions options)
        => env.IsDevelopment() || options.AcknowledgeNoUserAuth;
}

// Saved settings (the settings screen) win; with nothing saved, configuration decides (Reports:Provider and
// ConnectionStrings:Reporting). Checked on every call, so a saved change applies without a restart.
public sealed class ReportingSource(
    IDatabaseSettingsRepository repo, ReportingOptions options, string? configConnectionString, IHostEnvironment env)
    : IReportingSource
{
    public ReportingConfig Current()
    {
        var saved = repo.Get();
        if (saved is not null)
            return saved.Mode == DbMode.Sql
                ? Sql(saved.BuildConnectionString(), saved.PatientSummaryView, saved.FacilityMetricsView)
                : ReportingConfig.Synthetic;

        return options.Provider.Equals(ReportingOptions.Sql, StringComparison.OrdinalIgnoreCase)
            ? Sql(configConnectionString ?? "", options.PatientSummaryView, options.FacilityMetricsView)
            : ReportingConfig.Synthetic;
    }

    private ReportingConfig Sql(string connectionString, string patientView, string facilityView)
    {
        if (!ReportingGuard.SqlAllowed(env, options)) throw new InvalidOperationException(ReportingGuard.Refusal);
        return new ReportingConfig(true, connectionString, patientView, facilityView, options.CommandTimeoutSeconds);
    }
}

// The one IReportDataReader the app uses. It follows the source: demo data or a SQL reader, rebuilt only when
// the connection or view names change.
public sealed class SwitchableReportDataReader(IReportingSource source, ILoggerFactory loggers) : IReportDataReader
{
    private readonly SyntheticReportDataReader _synthetic = new();
    private readonly object _lock = new();
    private ReportingConfig? _cachedConfig;
    private SqlReportDataReader? _cached;

    public bool UsingSql => source.Current().UseSql;

    public Task<PatientSummary?> GetPatientSummaryAsync(long account, CancellationToken ct = default)
        => Pick().GetPatientSummaryAsync(account, ct);

    public Task<FacilityMetrics?> GetFacilityMetricsAsync(string facility, CancellationToken ct = default)
        => Pick().GetFacilityMetricsAsync(facility, ct);

    private IReportDataReader Pick()
    {
        var config = source.Current();
        if (!config.UseSql) return _synthetic;
        lock (_lock)
        {
            if (_cached is null || _cachedConfig != config)
            {
                var options = new ReportingOptions
                {
                    Provider = ReportingOptions.Sql,
                    PatientSummaryView = config.PatientSummaryView,
                    FacilityMetricsView = config.FacilityMetricsView,
                    CommandTimeoutSeconds = config.CommandTimeoutSeconds,
                };
                var connectionString = config.ConnectionString;
                _cached = new SqlReportDataReader(
                    () => new SqlConnection(connectionString), options, loggers.CreateLogger<SqlReportDataReader>());
                _cachedConfig = config;
            }
            return _cached;
        }
    }
}
