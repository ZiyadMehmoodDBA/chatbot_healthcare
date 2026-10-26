using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using AiChatbot.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Data;

// Read-only reader over two reporting views (contract: docs/Data_Dictionary.md). It never touches base tables:
// the data owner decides what the views expose. Queries are fixed text with bound parameters; only the view
// names are interpolated, and only after they pass the identifier check below.
public sealed partial class SqlReportDataReader : IReportDataReader
{
    public const int MaxFacilityLength = 100;

    private readonly Func<DbConnection> _open;
    private readonly ReportingOptions _options;
    private readonly ILogger _log;
    private readonly string _patientSql;
    private readonly string _facilitySql;

    public SqlReportDataReader(Func<DbConnection> open, ReportingOptions options, ILogger log)
    {
        _open = open;
        _options = options;
        _log = log;
        var patientView = ValidateViewName(options.PatientSummaryView, nameof(options.PatientSummaryView));
        var facilityView = ValidateViewName(options.FacilityMetricsView, nameof(options.FacilityMetricsView));
        _patientSql = $"SELECT TOP (1) Admissions, LastVisitDate, OpenIncidents FROM {patientView} WHERE AccountNumber = @account";
        _facilitySql = $"SELECT TOP (1) ReadmissionRatePct, AvgLengthOfStayDays, IncidentCount, AsOfDate FROM {facilityView} WHERE Facility = @facility";
    }

    // schema.name or name: letters, digits and underscores only, so it can never carry SQL
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}(\.[A-Za-z_][A-Za-z0-9_]{0,63}){0,2}$")]
    private static partial Regex ViewName();

    public static string ValidateViewName(string? name, string setting)
        => name is not null && ViewName().IsMatch(name)
            ? name
            : throw new InvalidOperationException($"Reports:{setting} is not a valid view name");

    public async Task<PatientSummary?> GetPatientSummaryAsync(long account, CancellationToken ct = default)
    {
        if (account < 1) return null;
        return await QueryAsync(_patientSql, cmd => AddParameter(cmd, "@account", DbType.Int64, account), r => new PatientSummary(
            Convert.ToInt32(r.GetValue(0), CultureInfo.InvariantCulture),
            ToIsoDate(r.GetValue(1)),
            Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture)), ct);
    }

    public async Task<FacilityMetrics?> GetFacilityMetricsAsync(string facility, CancellationToken ct = default)
    {
        var name = facility?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxFacilityLength) return null;
        return await QueryAsync(_facilitySql, cmd => AddParameter(cmd, "@facility", DbType.String, name, MaxFacilityLength), r => new FacilityMetrics(
            Convert.ToDouble(r.GetValue(0), CultureInfo.InvariantCulture),
            Convert.ToDouble(r.GetValue(1), CultureInfo.InvariantCulture),
            Convert.ToInt32(r.GetValue(2), CultureInfo.InvariantCulture),
            ToIsoDate(r.GetValue(3))), ct);
    }

    private async Task<T?> QueryAsync<T>(string sql, Action<DbCommand> bind, Func<DbDataReader, T> map, CancellationToken ct)
        where T : class
    {
        try
        {
            await using var conn = _open();
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = _options.CommandTimeoutSeconds;
            bind(cmd);
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
            return await reader.ReadAsync(ct) ? map(reader) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Type only: messages can echo values and connection details
            _log.LogError("Reporting query failed ({ExceptionType})", ex.GetType().Name);
            throw;
        }
    }

    private static void AddParameter(DbCommand cmd, string name, DbType type, object value, int size = 0)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.DbType = type;
        p.Value = value;
        if (size > 0) p.Size = size;
        cmd.Parameters.Add(p);
    }

    // NULL becomes "none"; dates (or date-like strings from other providers) become yyyy-MM-dd
    private static string ToIsoDate(object? v) => v switch
    {
        null or DBNull => "none",
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => DateTime.TryParse(v.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : "none",
    };
}
