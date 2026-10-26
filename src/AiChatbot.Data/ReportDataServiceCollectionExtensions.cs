using AiChatbot.Agent.Tools;
using Microsoft.AspNetCore.DataProtection;
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
    // provider therefore refuses to run unless an operator accepts that explicitly.
    public bool AcknowledgeNoUserAuth { get; set; }
}

public static class ReportDataServiceCollectionExtensions
{
    // Data comes from the saved database settings (settings screen) when there are any, otherwise from
    // configuration: Reports:Provider = Synthetic (default) | Sql, with ConnectionStrings:Reporting (a read-only
    // login limited to the two views; user-secrets / environment, never a repo file).
    // databaseSettingsPath enables saving from the settings screen; it needs AddDataProtection() to be registered.
    public static IServiceCollection AddReportData(
        this IServiceCollection services, IConfiguration config, IHostEnvironment env, string? databaseSettingsPath = null)
    {
        var options = config.GetSection("Reports").Get<ReportingOptions>() ?? new ReportingOptions();
        var connectionString = config.GetConnectionString("Reporting");

        if (!options.Provider.Equals(ReportingOptions.Synthetic, StringComparison.OrdinalIgnoreCase)
            && !options.Provider.Equals(ReportingOptions.Sql, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Reports:Provider must be 'Synthetic' or 'Sql'");

        // Configuration that asks for SQL must be complete: fail at startup, not on the first question
        SqlReportDataReader.ValidateViewName(options.PatientSummaryView, nameof(options.PatientSummaryView));
        SqlReportDataReader.ValidateViewName(options.FacilityMetricsView, nameof(options.FacilityMetricsView));
        if (options.Provider.Equals(ReportingOptions.Sql, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Reports:Provider=Sql needs ConnectionStrings:Reporting");
            if (!ReportingGuard.SqlAllowed(env, options))
                throw new InvalidOperationException(ReportingGuard.Refusal);
        }

        services.AddSingleton(options);
        services.AddSingleton<IDatabaseSettingsRepository>(sp => databaseSettingsPath is null
            ? new NullDatabaseSettingsRepository()
            : new JsonDatabaseSettingsRepository(
                databaseSettingsPath, sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("AiChatbot.DatabaseSettings.v1")));
        services.AddSingleton<IReportingSource>(sp =>
            new ReportingSource(sp.GetRequiredService<IDatabaseSettingsRepository>(), options, connectionString, env));
        services.AddSingleton<SwitchableReportDataReader>(sp =>
            new SwitchableReportDataReader(sp.GetRequiredService<IReportingSource>(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IReportDataReader>(sp => sp.GetRequiredService<SwitchableReportDataReader>());
        return services;
    }
}
