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
    private readonly string _claimVolume;
    private readonly string _daysToBill;
    private readonly string _denials;
    private readonly string _patientBilling;

    public ReportQueries(ReportServiceOptions options)
    {
        _payments = ValidateViewName(options.PaymentsView, nameof(options.PaymentsView));
        _insuranceAr = ValidateViewName(options.InsuranceArView, nameof(options.InsuranceArView));
        _claimVolume = ValidateViewName(options.ClaimVolumeView, nameof(options.ClaimVolumeView));
        _daysToBill = ValidateViewName(options.DaysToBillView, nameof(options.DaysToBillView));
        _denials = ValidateViewName(options.DenialsView, nameof(options.DenialsView));
        _patientBilling = ValidateViewName(options.PatientBillingView, nameof(options.PatientBillingView));
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

    // Claims by practice and rendering provider for a date-of-service period.
    public ReportQuery ClaimVolume(
        DateOnly start, DateOnly end, IReadOnlyList<long> practices, long? providerId, int topN, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        p.AddRange(DateRange(start, end));
        p.Add(new("@topN", DbType.Int32, topN));
        var where = new StringBuilder($"DOS >= @start AND DOS < DATEADD(DAY, 1, @end) AND Practice_Code IN ({scope})");
        Optional(where, p, "Rendering_Provider_ID", "@providerId", providerId);
        var sql = $"""
            SELECT TOP (@topN) Practice_Code, Rendering_Provider_ID, Rendering_Provider, COUNT(DISTINCT Claim_No), COUNT_BIG(*),
                   COALESCE(SUM(CAST(Charge_Amount AS decimal(19,4))), 0)
            FROM {_claimVolume}
            WHERE {where}
            GROUP BY Practice_Code, Rendering_Provider_ID, Rendering_Provider
            ORDER BY COUNT(DISTINCT Claim_No) DESC, Practice_Code, Rendering_Provider_ID
            """;
        return new(sql, p, topN, o.CommandTimeoutSeconds);
    }

    // Days to bill by payer. A claim counts only with both dates and a non-negative gap.
    public ReportQuery DaysToBill(
        DateOnly start, DateOnly end, IReadOnlyList<long> practices, long? payerId, int topN, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        p.AddRange(DateRange(start, end));
        p.Add(new("@topN", DbType.Int32, topN));
        var where = new StringBuilder(DaysToBillWhere(scope));
        Optional(where, p, "v.Ins_Payer_Id", "@payerId", payerId);
        var sql = $"""
            SELECT TOP (@topN) v.Ins_Payer_Id, v.Ins_Payer_Name, COUNT(DISTINCT v.Claim_No),
                   COALESCE(AVG(CAST(d.Days AS decimal(19,4))), 0), COALESCE(MAX(d.Days), 0)
            FROM {_daysToBill} v
            CROSS APPLY (SELECT DATEDIFF(DAY, v.DOS, v.First_Submission_Date) AS Days) d
            WHERE {where}
            GROUP BY v.Ins_Payer_Id, v.Ins_Payer_Name
            ORDER BY COUNT(DISTINCT v.Claim_No) DESC, v.Ins_Payer_Id
            """;
        return new(sql, p, topN, o.CommandTimeoutSeconds);
    }

    // The overall figure across payers, shown as the first row of the report
    public ReportQuery DaysToBillOverall(
        DateOnly start, DateOnly end, IReadOnlyList<long> practices, long? payerId, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        p.AddRange(DateRange(start, end));
        var where = new StringBuilder(DaysToBillWhere(scope));
        Optional(where, p, "v.Ins_Payer_Id", "@payerId", payerId);
        var sql = $"""
            SELECT COUNT(DISTINCT v.Claim_No), COALESCE(AVG(CAST(d.Days AS decimal(19,4))), 0), COALESCE(MAX(d.Days), 0)
            FROM {_daysToBill} v
            CROSS APPLY (SELECT DATEDIFF(DAY, v.DOS, v.First_Submission_Date) AS Days) d
            WHERE {where}
            """;
        return new(sql, p, 1, o.CommandTimeoutSeconds);
    }

    private static string DaysToBillWhere(string scope)
        => "v.First_Submission_Date >= @start AND v.First_Submission_Date < DATEADD(DAY, 1, @end) " +
           $"AND v.DOS IS NOT NULL AND DATEDIFF(DAY, v.DOS, v.First_Submission_Date) >= 0 AND v.Practice_Code IN ({scope})";

    // Denial and rejection status records by status and reason. Counts claims, never dollars.
    public ReportQuery Denials(
        DateOnly start, DateOnly end, IReadOnlyList<long> practices, long? payerId, int topN, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        p.AddRange(DateRange(start, end));
        p.Add(new("@topN", DbType.Int32, topN));
        var where = new StringBuilder(DenialsWhere(scope));
        Optional(where, p, "Ins_Payer_Id", "@payerId", payerId);
        var sql = $"""
            SELECT TOP (@topN) Claim_Status, Status_Reason, COUNT(DISTINCT Claim_No), COUNT_BIG(*), COUNT(DISTINCT Ins_Payer_Id)
            FROM {_denials}
            WHERE {where}
            GROUP BY Claim_Status, Status_Reason
            ORDER BY COUNT(DISTINCT Claim_No) DESC, Claim_Status, Status_Reason
            """;
        return new(sql, p, topN, o.CommandTimeoutSeconds);
    }

    public ReportQuery DenialsOverall(
        DateOnly start, DateOnly end, IReadOnlyList<long> practices, long? payerId, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        p.AddRange(DateRange(start, end));
        var where = new StringBuilder(DenialsWhere(scope));
        Optional(where, p, "Ins_Payer_Id", "@payerId", payerId);
        var sql = $"""
            SELECT COUNT(DISTINCT Claim_No), COUNT_BIG(*), COUNT(DISTINCT Ins_Payer_Id)
            FROM {_denials}
            WHERE {where}
            """;
        return new(sql, p, 1, o.CommandTimeoutSeconds);
    }

    private static string DenialsWhere(string scope)
        => $"Claim_Status_Date >= @start AND Claim_Status_Date < DATEADD(DAY, 1, @end) AND Practice_Code IN ({scope})";

    // One patient account. The account number is a bound parameter; the practice list is always the actor's scope.
    public ReportQuery PatientLines(long account, IReadOnlyList<long> practices, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        p.Add(new("@account", DbType.Int64, account));
        p.Add(new("@max", DbType.Int32, o.MaxPatientRows + 1));
        var sql = $"""
            SELECT TOP (@max) Practice_Code, Claim_No, Sequence_Number, DOS, CPT, Billed_Date, Billing_Status,
                   Charge_Amount, Insurance_Payments, Patient_Payments,
                   COALESCE(Contractual_Adjustments, 0) + COALESCE(Manual_Adjustments, 0),
                   Patient_Responsibility, Patient_Balance
            FROM {_patientBilling}
            WHERE Patient_Account = @account AND Practice_Code IN ({scope})
            ORDER BY DOS DESC, Claim_No, Sequence_Number
            """;
        return new(sql, p, o.MaxPatientRows, o.CommandTimeoutSeconds);
    }

    // Totals cover every line of the account, not only the lines shown
    public ReportQuery PatientTotals(long account, IReadOnlyList<long> practices, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        p.Add(new("@account", DbType.Int64, account));
        var sql = $"""
            SELECT COUNT_BIG(*), COUNT(DISTINCT Claim_No),
                   COALESCE(SUM(CAST(Charge_Amount AS decimal(19,4))), 0),
                   COALESCE(SUM(CAST(Insurance_Payments AS decimal(19,4))), 0),
                   COALESCE(SUM(CAST(Patient_Payments AS decimal(19,4))), 0),
                   COALESCE(SUM(CAST(COALESCE(Contractual_Adjustments, 0) + COALESCE(Manual_Adjustments, 0) AS decimal(19,4))), 0),
                   COALESCE(SUM(CAST(Patient_Responsibility AS decimal(19,4))), 0),
                   COALESCE(SUM(CAST(Patient_Balance AS decimal(19,4))), 0)
            FROM {_patientBilling}
            WHERE Patient_Account = @account AND Practice_Code IN ({scope})
            """;
        return new(sql, p, 1, o.CommandTimeoutSeconds);
    }

    // Latest date in the source, so a reader can see how fresh the data is. Scoped like the report itself.
    public ReportQuery Freshness(string reportId, IReadOnlyList<long> practices, ReportServiceOptions o)
    {
        var (p, scope) = Scope(practices);
        var (column, view) = reportId switch
        {
            ReportCatalog.InsuranceAging => ("Billed_Date", _insuranceAr),
            ReportCatalog.ClaimVolume => ("DOS", _claimVolume),
            ReportCatalog.DaysToBill => ("First_Submission_Date", _daysToBill),
            ReportCatalog.Denials => ("Claim_Status_Date", _denials),
            ReportCatalog.PatientBilling => ("Billed_Date", _patientBilling),
            _ => ("Posting_Date", _payments),
        };
        return new($"SELECT MAX({column}) FROM {view} WHERE Practice_Code IN ({scope})", p, 1, o.CommandTimeoutSeconds);
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
