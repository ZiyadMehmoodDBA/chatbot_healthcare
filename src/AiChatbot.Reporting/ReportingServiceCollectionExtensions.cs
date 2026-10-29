using System.Data.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Reporting;

public static class ReportingServiceCollectionExtensions
{
    // openConnection returns a connection for a read-only login limited to the reporting views, or throws a
    // ReportException(Unavailable) when no database is configured.
    public static IServiceCollection AddReporting(
        this IServiceCollection services, IConfiguration config, Func<IServiceProvider, Func<DbConnection>> openConnection)
    {
        var options = config.GetSection("Reporting").Get<ReportServiceOptions>() ?? new ReportServiceOptions();
        // Fail at startup on bad view names, not on the first report
        _ = new ReportQueries(options);
        services.AddSingleton(options);
        services.AddSingleton<IReportAudit, LoggerReportAudit>();
        services.AddSingleton<IReportExecutor>(sp =>
            new SqlReportExecutor(openConnection(sp), sp.GetRequiredService<ILogger<SqlReportExecutor>>()));
        services.AddSingleton<ReportBroker>();
        return services;
    }
}
