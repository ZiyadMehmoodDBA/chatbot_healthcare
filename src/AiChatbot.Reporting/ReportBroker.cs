using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Logging;

namespace AiChatbot.Reporting;

public sealed class ReportServiceOptions
{
    public string PaymentsView { get; set; } = "reporting.vw_Rpt_Payments";
    public string InsuranceArView { get; set; } = "reporting.vw_Rpt_InsuranceAr";
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int MaxRangeDays { get; set; } = 366;
    public int DefaultTopN { get; set; } = 10;
    public int MaxTopN { get; set; } = 100;
}

public interface IReportExecutor
{
    Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery query, CancellationToken ct);
}

// Runs one fixed query on a read-only connection. Failures become "unavailable" and never echo driver text,
// which can carry values and connection details.
public sealed class SqlReportExecutor(Func<DbConnection> open, ILogger<SqlReportExecutor> log) : IReportExecutor
{
    public async Task<IReadOnlyList<object?[]>> ExecuteAsync(ReportQuery query, CancellationToken ct)
    {
        try
        {
            await using var conn = open();
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = query.Sql;
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = query.TimeoutSeconds;
            foreach (var q in query.Parameters)
            {
                var p = cmd.CreateParameter();
                p.ParameterName = q.Name;
                p.DbType = q.Type;
                p.Value = q.Value;
                cmd.Parameters.Add(p);
            }
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var rows = new List<object?[]>();
            while (rows.Count < query.MaxRows + 1 && await reader.ReadAsync(ct))
            {
                var values = new object?[reader.FieldCount];
                reader.GetValues(values!);
                for (var i = 0; i < values.Length; i++) if (values[i] is DBNull) values[i] = null;
                rows.Add(values);
            }
            return rows;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ReportException)
        {
            log.LogError("Report query failed ({ExceptionType})", ex.GetType().Name);
            throw new ReportException(ReportErrorKind.Unavailable, "The reporting data source is unavailable");
        }
    }
}

// The one way to run a report: authorize, validate, build the fixed query, run it, compute shares in code, attach
// evidence, audit. The caller's practice list can only narrow the actor's scope, never widen it.
public sealed class ReportBroker(
    IReportExecutor executor, IReportAudit audit, ReportServiceOptions options, Func<DateTime>? clock = null)
{
    private readonly ReportQueries _queries = new(options);
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);

    public IReadOnlyList<ReportDefinition> CatalogFor(Actor actor)
        => ReportCatalog.All.Where(r => r.AllowedRoles.Contains(actor.Role)).ToList();

    // action: "run" or "export". Export goes through the same checks again.
    public async Task<ReportResult> RunAsync(
        Actor? actor, string reportId, ReportRequest request, string action, string correlationId, CancellationToken ct = default)
    {
        var started = Stopwatch.StartNew();
        var filters = new Dictionary<string, string>();
        ReportDefinition? def = null;
        try
        {
            if (actor is null) throw new ReportException(ReportErrorKind.Unauthenticated, "Sign in is required");
            def = ReportCatalog.Find(reportId) ?? throw new ReportException(ReportErrorKind.NotFound, "Unknown report");
            if (!def.AllowedRoles.Contains(actor.Role))
                throw new ReportException(ReportErrorKind.Forbidden, "Your role cannot run this report");

            var practices = EffectivePractices(actor, request.PracticeCodes);
            var topN = request.TopN ?? options.DefaultTopN;
            if (topN < 1 || topN > options.MaxTopN)
                throw new ReportException(ReportErrorKind.Invalid, $"topN must be between 1 and {options.MaxTopN}");
            var basis = (request.DateBasis ?? def.DefaultDateBasis).ToLowerInvariant();
            if (!def.DateBases.Contains(basis))
                throw new ReportException(ReportErrorKind.Invalid, $"dateBasis must be one of: {string.Join(", ", def.DateBases)}");

            filters["practices"] = string.Join(",", practices);
            filters["dateBasis"] = basis;
            if (request.PayerId is not null) filters["payerId"] = request.PayerId.Value.ToString(CultureInfo.InvariantCulture);
            if (request.ProviderId is not null) filters["providerId"] = request.ProviderId.Value.ToString(CultureInfo.InvariantCulture);

            ReportQuery query;
            string period;
            if (def.Id == ReportCatalog.InsuranceAging)
            {
                var asOf = AsOf(request);
                period = $"as of {asOf:yyyy-MM-dd}";
                filters["asOfDate"] = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                filters["topN"] = topN.ToString(CultureInfo.InvariantCulture);
                query = _queries.InsuranceAging(asOf, practices, request.PayerId, topN, options);
            }
            else
            {
                var (start, end) = Range(request);
                period = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}";
                filters["startDate"] = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                filters["endDate"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (def.Id == ReportCatalog.InsuranceVsPatient)
                    query = _queries.InsuranceVsPatient(basis, start, end, practices, request.PayerId, request.ProviderId, options);
                else
                {
                    filters["topN"] = topN.ToString(CultureInfo.InvariantCulture);
                    query = _queries.PaymentsByPayer(basis, start, end, practices, request.ProviderId, topN, options);
                }
            }

            var raw = await executor.ExecuteAsync(query, ct);
            var truncated = raw.Count > query.MaxRows;
            var rows = raw.Take(query.MaxRows).Select(r => Shape(def.Id, r)).ToList();
            var through = await DataThroughAsync(def.Id, practices, ct);
            var caveats = def.Caveats.ToList();
            var periodEnd = def.Id == ReportCatalog.InsuranceAging ? AsOf(request) : request.EndDate;
            if (through is { } t && periodEnd is { } pe && pe > t)
                caveats.Insert(0, $"The source data ends {t:yyyy-MM-dd}, before the end of the requested period; later activity is not included.");
            var evidence = new ReportEvidence(
                def.Id, def.MetricVersion, def.ApprovalStatus, def.Source, basis, period, filters,
                rows.Count, truncated, _now(), caveats, through);

            Record(action, actor, def.Id, filters, "ok", rows.Count, started, correlationId);
            return new ReportResult(def.Columns, rows, evidence);
        }
        catch (ReportException ex)
        {
            Record(action, actor, def?.Id ?? reportId, filters, ex.Kind.ToString(), 0, started, correlationId);
            throw;
        }
    }

    private DateOnly AsOf(ReportRequest r) => r.AsOfDate ?? DateOnly.FromDateTime(_now());

    private async Task<DateOnly?> DataThroughAsync(string reportId, IReadOnlyList<long> practices, CancellationToken ct)
    {
        var rows = await executor.ExecuteAsync(_queries.Freshness(reportId, practices, options), ct);
        return rows.Count > 0 && rows[0][0] is { } v
            ? DateOnly.FromDateTime(Convert.ToDateTime(v, CultureInfo.InvariantCulture))
            : null;
    }

    private void Record(string action, Actor? actor, string reportId, Dictionary<string, string> filters, string outcome,
        int rows, Stopwatch sw, string correlationId)
        => audit.Record(new ReportAuditEntry(
            _now(), correlationId, action, actor?.UserId ?? "anonymous", actor?.Role ?? "none", reportId,
            filters, outcome, rows, sw.ElapsedMilliseconds));

    private static List<long> EffectivePractices(Actor actor, IReadOnlyList<long>? requested)
    {
        if (actor.Practices.Count == 0)
            throw new ReportException(ReportErrorKind.Forbidden, "No practices are assigned to this account");
        if (requested is null || requested.Count == 0) return actor.Practices.Distinct().Order().ToList();
        var asked = requested.Distinct().Order().ToList();
        if (asked.Any(p => !actor.Practices.Contains(p)))
            throw new ReportException(ReportErrorKind.Forbidden, "A requested practice is outside your access");
        return asked;
    }

    private (DateOnly Start, DateOnly End) Range(ReportRequest r)
    {
        if (r.StartDate is null || r.EndDate is null)
            throw new ReportException(ReportErrorKind.Invalid, "startDate and endDate are required");
        var (s, e) = (r.StartDate.Value, r.EndDate.Value);
        if (e < s) throw new ReportException(ReportErrorKind.Invalid, "endDate must not be before startDate");
        if (e.DayNumber - s.DayNumber + 1 > options.MaxRangeDays)
            throw new ReportException(ReportErrorKind.Invalid, $"The date range may not exceed {options.MaxRangeDays} days");
        return (s, e);
    }

    // Sums come from SQL; shares, totals and differences are computed here in decimal, never by the model
    private static IReadOnlyList<object?> Shape(string id, object?[] r) => id switch
    {
        ReportCatalog.InsuranceVsPatient => InsuranceVsPatientRow(r),
        ReportCatalog.PaymentsByPayer => [Text(r[0]), Text(r[1]), Int(r[2]), Dec(r[3]), Dec(r[4]), Dec(r[3]) + Dec(r[4])],
        _ => [Text(r[0]), Text(r[1]), Int(r[2]), Dec(r[3]), Dec(r[4]), Dec(r[5]), Dec(r[6]), Dec(r[7]), Dec(r[8]),
              Dec(r[3]) + Dec(r[4]) + Dec(r[5]) + Dec(r[6]) + Dec(r[7]) + Dec(r[8])],
    };

    private static object?[] InsuranceVsPatientRow(object?[] r)
    {
        var ins = Dec(r[2]);
        var pat = Dec(r[3]);
        var combined = ins + pat;
        return [Int(r[0]), Int(r[1]), ins, pat, combined, ins - pat, Pct(ins, combined), Pct(pat, combined)];
    }

    public static decimal? Pct(decimal part, decimal whole)
        => whole == 0 ? null : Math.Round(part / whole * 100m, 2, MidpointRounding.AwayFromZero);

    private static decimal Dec(object? v) => v is null ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    private static long Int(object? v) => v is null ? 0 : Convert.ToInt64(v, CultureInfo.InvariantCulture);
    private static string Text(object? v) => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
}
