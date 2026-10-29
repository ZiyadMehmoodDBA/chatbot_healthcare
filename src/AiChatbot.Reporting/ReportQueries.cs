using System.Data;
using System.Text;
using System.Text.RegularExpressions;

namespace AiChatbot.Reporting;

public sealed record QueryParameter(string Name, DbType Type, object Value);

public sealed record ReportQuery(string Sql, IReadOnlyList<QueryParameter> Parameters, int MaxRows, int TimeoutSeconds);

// Fixed query text per report. Nothing from the caller or the model reaches the SQL text: the view names pass the
// identifier check, the date column comes from a two-entry allowlist, and every value is a bound parameter.
public sealed partial class ReportQueries
{
    public const int MaxPracticeScope = 500;

    private readonly string _payments;
    private readonly string _insuranceAr;

    public ReportQueries(ReportServiceOptions options)
    {
        _payments = ValidateViewName(options.PaymentsView, nameof(options.PaymentsView));
        _insuranceAr = ValidateViewName(options.InsuranceArView, nameof(options.InsuranceArView));
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]{0,63}(\.[A-Za-z_][A-Za-z0-9_]{0,63}){0,2}$")]
    private static partial Regex ViewName();

    public static string ValidateViewName(string? name, string setting)
        => name is not null && ViewName().IsMatch(name)
            ? name
            : throw new InvalidOperationException($"Reporting:{setting} is not a valid view name");

    public ReportQuery InsuranceVsPatient(
        string dateBasis, DateOnly start, DateOnly end, IReadOnlyList<long> practices, long? payerId, long? providerId,
        ReportServiceOptions o)
    {
        var col = DateColumn(dateBasis);
        var (p, scope) = Scope(practices);
        p.AddRange(DateRange(start, end));
        var where = new StringBuilder($"{col} >= @start AND {col} < DATEADD(DAY, 1, @end) AND Practice_Code IN ({scope})");
        Optional(where, p, "Payer_Id", "@payerId", payerId);
        Optional(where, p, "Rendering_Provider_ID", "@providerId", providerId);
        var sql = $"""
            SELECT COUNT(DISTINCT Claim_No), COUNT_BIG(*),
                   COALESCE(SUM(CAST(InsurancePayments AS decimal(19,4))), 0),
                   COALESCE(SUM(CAST(PatientPayments AS decimal(19,4))), 0)
            FROM {_payments}
            WHERE {where}
            """;
        return new(sql, p, 1, o.CommandTimeoutSeconds);
    }

    public ReportQuery PaymentsByPayer(
        string dateBasis, DateOnly start, DateOnly end, IReadOnlyList<long> practices, long? providerId, int topN,
        ReportServiceOptions o)
    {
        var col = DateColumn(dateBasis);
        var (p, scope) = Scope(practices);
        p.AddRange(DateRange(start, end));
        p.Add(new("@topN", DbType.Int32, topN));
        var where = new StringBuilder($"{col} >= @start AND {col} < DATEADD(DAY, 1, @end) AND Practice_Code IN ({scope})");
        Optional(where, p, "Rendering_Provider_ID", "@providerId", providerId);
        var sql = $"""
            SELECT TOP (@topN) Payer_Id, Payer_Name, COUNT(DISTINCT Claim_No),
                   COALESCE(SUM(CAST(InsurancePayments AS decimal(19,4))), 0),
                   COALESCE(SUM(CAST(PatientPayments AS decimal(19,4))), 0)
            FROM {_payments}
            WHERE {where}
            GROUP BY Payer_Id, Payer_Name
            ORDER BY COALESCE(SUM(CAST(InsurancePayments AS decimal(19,4))), 0)
                   + COALESCE(SUM(CAST(PatientPayments AS decimal(19,4))), 0) DESC, Payer_Id
            """;
        return new(sql, p, topN, o.CommandTimeoutSeconds);
    }

    public ReportQuery InsuranceAging(
        DateOnly asOf, IReadOnlyList<long> practices, long? payerId, int topN, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        p.Add(new("@asOf", DbType.Date, asOf.ToDateTime(TimeOnly.MinValue)));
        p.Add(new("@topN", DbType.Int32, topN));
        var where = new StringBuilder(
            $"v.Insurance_Balance > 0 AND (v.Billed_Date IS NULL OR v.Billed_Date <= @asOf) AND v.Practice_Code IN ({scope})");
        Optional(where, p, "v.Ins_Payer_Id", "@payerId", payerId);
        var sql = $"""
            SELECT TOP (@topN) v.Ins_Payer_Id, v.Ins_Payer_Name, COUNT(DISTINCT v.Claim_No),
                   COALESCE(SUM(CASE WHEN a.Age BETWEEN 0 AND 30 THEN CAST(v.Insurance_Balance AS decimal(19,4)) END), 0),
                   COALESCE(SUM(CASE WHEN a.Age BETWEEN 31 AND 60 THEN CAST(v.Insurance_Balance AS decimal(19,4)) END), 0),
                   COALESCE(SUM(CASE WHEN a.Age BETWEEN 61 AND 90 THEN CAST(v.Insurance_Balance AS decimal(19,4)) END), 0),
                   COALESCE(SUM(CASE WHEN a.Age BETWEEN 91 AND 120 THEN CAST(v.Insurance_Balance AS decimal(19,4)) END), 0),
                   COALESCE(SUM(CASE WHEN a.Age > 120 THEN CAST(v.Insurance_Balance AS decimal(19,4)) END), 0),
                   COALESCE(SUM(CASE WHEN a.Age IS NULL THEN CAST(v.Insurance_Balance AS decimal(19,4)) END), 0)
            FROM {_insuranceAr} v
            CROSS APPLY (SELECT DATEDIFF(DAY, v.Billed_Date, @asOf) AS Age) a
            WHERE {where}
            GROUP BY v.Ins_Payer_Id, v.Ins_Payer_Name
            ORDER BY SUM(CAST(v.Insurance_Balance AS decimal(19,4))) DESC, v.Ins_Payer_Id
            """;
        return new(sql, p, topN, o.CommandTimeoutSeconds);
    }

    // Latest date in the source, so a reader can see how fresh the data is. Scoped like the report itself.
    public ReportQuery Freshness(string reportId, IReadOnlyList<long> practices, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        var sql = reportId == ReportCatalog.InsuranceAging
            ? $"SELECT MAX(Billed_Date) FROM {_insuranceAr} WHERE Practice_Code IN ({scope})"
            : $"SELECT MAX(Posting_Date) FROM {_payments} WHERE Practice_Code IN ({scope})";
        return new(sql, p, 1, o.CommandTimeoutSeconds);
    }

    // Allowlist, not interpolation of the request value
    private static string DateColumn(string basis) => basis switch
    {
        ReportCatalog.Posting => "Posting_Date",
        ReportCatalog.Deposit => "Deposit_Date",
        _ => throw new ReportException(ReportErrorKind.Invalid, "Unsupported date basis"),
    };

    private static (List<QueryParameter> Parameters, string InList) Scope(IReadOnlyList<long> practices)
    {
        if (practices.Count == 0) throw new ReportException(ReportErrorKind.Forbidden, "No practice scope");
        if (practices.Count > MaxPracticeScope) throw new InvalidOperationException("Practice scope is too large");
        var p = new List<QueryParameter>();
        for (var i = 0; i < practices.Count; i++) p.Add(new($"@p{i}", DbType.Int64, practices[i]));
        return (p, string.Join(", ", p.Select(x => x.Name)));
    }

    private static QueryParameter[] DateRange(DateOnly start, DateOnly end) =>
    [
        new("@start", DbType.Date, start.ToDateTime(TimeOnly.MinValue)),
        new("@end", DbType.Date, end.ToDateTime(TimeOnly.MinValue)),
    ];

    private static void Optional(StringBuilder where, List<QueryParameter> p, string column, string name, long? value)
    {
        if (value is null) return;
        where.Append($" AND {column} = {name}");
        p.Add(new(name, DbType.Int64, value.Value));
    }
}
