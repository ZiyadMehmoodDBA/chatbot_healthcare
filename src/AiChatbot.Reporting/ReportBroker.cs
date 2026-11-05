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
    public string ClaimVolumeView { get; set; } = "reporting.vw_Rpt_ClaimVolume";
    public string DaysToBillView { get; set; } = "reporting.vw_Rpt_DaysToBill";
    public string DenialsView { get; set; } = "reporting.vw_Rpt_Denials";
    public string PatientBillingView { get; set; } = "reporting.vw_Rpt_PatientBilling";
    // Claim lines shown for one patient account; the totals row always covers every line
    public int MaxPatientRows { get; set; } = 200;
    // Secret for the audit account reference (HMAC key). Unset: a random key per process, so references are not
    // stable across restarts but can never be reversed. Set it (key vault or configuration) to correlate across restarts.
    public string? AccountRefKey { get; set; }
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
    private readonly byte[] _refKey = !string.IsNullOrEmpty(options.AccountRefKey)
        ? System.Text.Encoding.UTF8.GetBytes(options.AccountRefKey) : ProcessKey;
    private static readonly byte[] ProcessKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);

    public IReadOnlyList<ReportDefinition> CatalogFor(Actor actor)
        => ReportCatalog.All.Where(r => !r.PatientLevel && r.AllowedRoles.Contains(actor.Role)).ToList();

    // action: "run" or "export". Export goes through the same checks again.
    public int MaxTopN => options.MaxTopN;

    // Two runs of the same report, side by side. Each run goes through RunAsync, so role, practice scope, limits and
    // audit apply to both. Nothing here widens access.
    public async Task<ReportResult> CompareAsync(
        Actor? actor, string reportId, ReportRequest current, ReportRequest prior, string action, string correlationId,
        IReadOnlyList<string>? extraCaveats = null, CancellationToken ct = default)
    {
        if (!ComparisonBuilder.CanCompare(reportId))
            throw new ReportException(ReportErrorKind.Invalid, "This report cannot be compared across periods");
        var cur = await RunAsync(actor, reportId, current, action, correlationId, ct);
        var pri = await RunAsync(actor, reportId, prior, action, correlationId, ct);
        // A period that runs past the loaded data compares a partial period with a full one
        static bool Past(ReportRequest q, DateOnly? through)
            => through is { } t && (q.AsOfDate ?? q.EndDate) is { } e && e > t;
        var c = Past(current, cur.Evidence.DataThrough);
        var p = Past(prior, pri.Evidence.DataThrough);
        return ComparisonBuilder.Build(cur, pri, extraCaveats, c && p ? "both" : c ? "current" : p ? "prior" : null);
    }

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
            ReportQuery? extra = null;
            string period;
            DateOnly? periodEnd = request.EndDate;
            if (def.Id == ReportCatalog.PatientBilling)
            {
                if (request.AccountNumber is not { } account || account <= 0)
                    throw new ReportException(ReportErrorKind.Invalid, "A patient account number is required");
                // The number itself never reaches evidence or audit: a short hash lets support match repeat lookups
                filters["accountRef"] = AccountRef(account);
                period = "current balance";
                periodEnd = null;
                query = _queries.PatientLines(account, practices, options);
                extra = _queries.PatientTotals(account, practices, options);
            }
            else if (def.Id == ReportCatalog.InsuranceAging)
            {
                var asOf = AsOf(request);
                period = $"as of {asOf:yyyy-MM-dd}";
                periodEnd = asOf;
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
                if (def.Id != ReportCatalog.InsuranceVsPatient) filters["topN"] = topN.ToString(CultureInfo.InvariantCulture);
                switch (def.Id)
                {
                    case ReportCatalog.InsuranceVsPatient:
                        query = _queries.InsuranceVsPatient(basis, start, end, practices, request.PayerId, request.ProviderId, options);
                        break;
                    case ReportCatalog.PaymentsByPayer:
                        query = _queries.PaymentsByPayer(basis, start, end, practices, request.ProviderId, topN, options);
                        break;
                    case ReportCatalog.ClaimVolume:
                        query = _queries.ClaimVolume(start, end, practices, request.ProviderId, topN, options);
                        break;
                    case ReportCatalog.DaysToBill:
                        query = _queries.DaysToBill(start, end, practices, request.PayerId, topN, options);
                        extra = _queries.DaysToBillOverall(start, end, practices, request.PayerId, options);
                        break;
                    case ReportCatalog.Denials:
                        query = _queries.Denials(start, end, practices, request.PayerId, topN, options);
                        extra = _queries.DenialsOverall(start, end, practices, request.PayerId, options);
                        break;
                    default:
                        throw new ReportException(ReportErrorKind.NotFound, "Unknown report");
                }
            }

            var raw = await executor.ExecuteAsync(query, ct);
            var extraRows = extra is null ? null : await executor.ExecuteAsync(extra, ct);
            var truncated = raw.Count > query.MaxRows;
            var rows = ShapeAll(def.Id, raw.Take(query.MaxRows).ToList(), extraRows);
            var through = await DataThroughAsync(def.Id, practices, ct);
            var caveats = def.Caveats.ToList();
            if (through is { } t && periodEnd is { } pe && pe > t)
                caveats.Insert(0, $"The source data ends {t:yyyy-MM-dd}, before the end of the requested period; later activity is not included.");
            if (truncated && def.Id == ReportCatalog.PatientBilling)
                caveats.Insert(0, $"Showing the newest {options.MaxPatientRows} claim lines; the total row covers every line.");
            var evidence = new ReportEvidence(
                def.Id, def.MetricVersion, MetricRegistry.Default.StatusFor(def.Id) ?? def.ApprovalStatus, def.Source, basis, period, filters,
                rows.Count, truncated, _now(), caveats, through);

            Record(action, actor, def.Id, filters, "ok", rows.Count, started, correlationId);
            return new ReportResult(def.Columns, rows, evidence);
        }
        catch (ReportException ex)
        {
            Record(action, actor, def?.Id ?? ReportCatalog.AuditId(reportId), filters, ex.Kind.ToString(), 0, started, correlationId);
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

    // Keyed reference for audit and evidence: first 10 hex characters of HMAC-SHA-256(key, account). Enough to spot a
    // repeat lookup; without the key it cannot be recomputed from an account number (a plain hash of a short number can).
    public string AccountRef(long account)
        => Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(
            _refKey, System.Text.Encoding.UTF8.GetBytes(account.ToString(CultureInfo.InvariantCulture))))[..10].ToLowerInvariant();

    // Sums come from SQL; shares, totals and differences are computed here in decimal, never by the model.
    // Reports with an overall figure or a totals row take it from the second query.
    private static List<IReadOnlyList<object?>> ShapeAll(string id, List<object?[]> raw, IReadOnlyList<object?[]>? extra)
    {
        var rows = raw.Select(r => Shape(id, r)).ToList();
        switch (id)
        {
            case ReportCatalog.DaysToBill when extra is { Count: > 0 } && Int(extra[0][0]) > 0:
                rows.Insert(0, ["", "All payers", Int(extra[0][0]), Dec(extra[0][1]), Int(extra[0][2])]);
                break;
            case ReportCatalog.Denials when extra is { Count: > 0 } && Int(extra[0][0]) > 0:
                rows.Insert(0, ["Any status", "All status entries", Int(extra[0][0]), Int(extra[0][1]), Int(extra[0][2])]);
                break;
            case ReportCatalog.PatientBilling when rows.Count > 0 && extra is { Count: > 0 }:
                var t = extra[0];
                rows.Add(["Total", $"{Int(t[1]).ToString("N0", CultureInfo.InvariantCulture)} claim{(Int(t[1]) == 1 ? "" : "s")}", Int(t[0]), "", "", "", "",
                    Dec(t[2]), Dec(t[3]), Dec(t[4]), Dec(t[5]), Dec(t[6]), Dec(t[7])]);
                break;
        }
        return rows;
    }

    private static IReadOnlyList<object?> Shape(string id, object?[] r) => id switch
    {
        ReportCatalog.InsuranceVsPatient => InsuranceVsPatientRow(r),
        ReportCatalog.PaymentsByPayer => [Text(r[0]), Text(r[1]), Int(r[2]), Dec(r[3]), Dec(r[4]), Dec(r[3]) + Dec(r[4])],
        ReportCatalog.ClaimVolume => [Text(r[0]), Text(r[1]), Text(r[2]), Int(r[3]), Int(r[4]), Dec(r[5])],
        ReportCatalog.DaysToBill => [Text(r[0]), Text(r[1]), Int(r[2]), Dec(r[3]), Int(r[4])],
        ReportCatalog.Denials => [Text(r[0]), Text(r[1]), Int(r[2]), Int(r[3]), Int(r[4])],
        ReportCatalog.PatientBilling => [Text(r[0]), Text(r[1]), Int(r[2]), Day(r[3]), Text(r[4]), Day(r[5]), Text(r[6]),
            Dec(r[7]), Dec(r[8]), Dec(r[9]), Dec(r[10]), Dec(r[11]), Dec(r[12])],
        _ => [Text(r[0]), Text(r[1]), Int(r[2]), Dec(r[3]), Dec(r[4]), Dec(r[5]), Dec(r[6]), Dec(r[7]), Dec(r[8]),
              Dec(r[3]) + Dec(r[4]) + Dec(r[5]) + Dec(r[6]) + Dec(r[7]) + Dec(r[8])],
    };

    private static string Day(object? v)
        => v is null ? "" : Convert.ToDateTime(v, CultureInfo.InvariantCulture).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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
