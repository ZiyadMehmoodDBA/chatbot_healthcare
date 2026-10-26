using AiChatbot.Agent.Tools;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Data;

// Reports section of configuration. Defaults keep the demo (synthetic) reader.
public sealed class ReportingOptions
{
    public const string Synthetic = "Synthetic";
    public const string Sql = "Sql";

    public string Provider { get; set; } = Synthetic;
    public string PatientSummaryView { get; set; } = "reporting.vw_PatientSummary";
    public string FacilityMetricsView { get; set; } = "reporting.vw_FacilityMetrics";
    public int CommandTimeoutSeconds { get; set; } = 10;

    // This API has no user authentication yet, so any caller could read any account. Outside Development the SQL
    // provider therefore refuses to start unless an operator accepts that explicitly.
    public bool AcknowledgeNoUserAuth { get; set; }
}

public static class ReportDataServiceCollectionExtensions
{
    // Reports:Provider = Synthetic (default) | Sql. Sql needs ConnectionStrings:Reporting, which should be a
    // read-only login limited to the two views (user-secrets / environment, never a file in the repo).
    public static IServiceCollection AddReportData(
        this IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        var options = config.GetSection("Reports").Get<ReportingOptions>() ?? new ReportingOptions();

        if (options.Provider.Equals(ReportingOptions.Synthetic, StringComparison.OrdinalIgnoreCase))
            return services.AddSingleton<IReportDataReader, SyntheticReportDataReader>();

        if (!options.Provider.Equals(ReportingOptions.Sql, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Reports:Provider must be 'Synthetic' or 'Sql'");

        var connectionString = config.GetConnectionString("Reporting");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Reports:Provider=Sql needs ConnectionStrings:Reporting");
        if (!env.IsDevelopment() && !options.AcknowledgeNoUserAuth)
            throw new InvalidOperationException(
                "Reports:Provider=Sql is refused outside Development: the API has no user authentication. " +
                "Add authentication, or set Reports:AcknowledgeNoUserAuth=true to accept the risk.");

        // Fail at startup, not on the first question
        SqlReportDataReader.ValidateViewName(options.PatientSummaryView, nameof(options.PatientSummaryView));
        SqlReportDataReader.ValidateViewName(options.FacilityMetricsView, nameof(options.FacilityMetricsView));

        return services.AddSingleton<IReportDataReader>(sp => new SqlReportDataReader(
            () => new SqlConnection(connectionString),
            options,
            sp.GetRequiredService<ILoggerFactory>().CreateLogger<SqlReportDataReader>()));
    }
}
