namespace AiChatbot.Reporting;

public enum GroupedSource { Payments, ClaimVolume, Denials }

public sealed record GroupDef(string Name, string Label, string Sql);

public sealed record MeasureDef(string Name, string Label, ColumnType Type, string Sql);

// A report defined by data: which source view, which grouping and which measures. The SQL fragments are literals in this
// file (never built from a caller or a model), the practice scope and every value are still bound parameters, and the
// broker still does the role check, scope, evidence and audit. Adding a grouping = adding a spec here plus tests.
public sealed record GroupedSpec(
    string Id,
    string Name,
    string Purpose,
    GroupedSource Source,
    IReadOnlyList<GroupDef> Groups,
    IReadOnlyList<MeasureDef> Measures,
    string OrderBy,
    // Month and similar groupings read best oldest first
    bool Chronological,
    string CaveatKey)
{
    // {date} in a group expression is replaced by the source's date column
    public string DateSqlToken => "{date}";
}

public static class GroupedReports
{
    public const string PaymentsByPractice = "payments-by-practice";
    public const string PaymentsByMonth = "payments-by-month";
    public const string ClaimVolumeByPractice = "claim-volume-by-practice";
    public const string ClaimVolumeByMonth = "claim-volume-by-month";
    public const string DenialsByPayer = "denials-by-payer";
    public const string DenialsByMonth = "denials-by-month";

    private static readonly GroupDef Practice = new("practiceCode", "Practice", "Practice_Code");
    private static readonly GroupDef Month = new("month", "Month", "CONVERT(char(7), {date}, 120)");

    private static readonly MeasureDef[] PaymentMeasures =
    [
        new("claimCount", "Unique claims", ColumnType.Integer, "COUNT(DISTINCT Claim_No)"),
        new("insurancePayments", "Insurance payments", ColumnType.Money, "COALESCE(SUM(CAST(InsurancePayments AS decimal(19,4))), 0)"),
        new("patientPayments", "Patient payments", ColumnType.Money, "COALESCE(SUM(CAST(PatientPayments AS decimal(19,4))), 0)"),
        new("combinedPayments", "Combined payments", ColumnType.Money,
            "COALESCE(SUM(CAST(InsurancePayments AS decimal(19,4))), 0) + COALESCE(SUM(CAST(PatientPayments AS decimal(19,4))), 0)"),
    ];

    private static readonly MeasureDef[] ClaimMeasures =
    [
        new("claimCount", "Unique claims", ColumnType.Integer, "COUNT(DISTINCT Claim_No)"),
        new("lineCount", "Claim lines", ColumnType.Integer, "COUNT_BIG(*)"),
        new("charges", "Charges", ColumnType.Money, "COALESCE(SUM(CAST(Charge_Amount AS decimal(19,4))), 0)"),
    ];

    private static readonly MeasureDef[] DenialMeasures =
    [
        new("claimCount", "Unique claims", ColumnType.Integer, "COUNT(DISTINCT Claim_No)"),
        new("recordCount", "Status records", ColumnType.Integer, "COUNT_BIG(*)"),
    ];

    private static readonly GroupDef Status = new("status", "Status", "Claim_Status");

    public static readonly IReadOnlyList<GroupedSpec> All =
    [
        new(PaymentsByPractice, "Payments by practice",
            "Insurance and patient payments received in a posting-date period, by practice.",
            GroupedSource.Payments, [Practice], PaymentMeasures,
            "COALESCE(SUM(CAST(InsurancePayments AS decimal(19,4))), 0) + COALESCE(SUM(CAST(PatientPayments AS decimal(19,4))), 0) DESC, Practice_Code",
            false, "payments"),
        new(PaymentsByMonth, "Payments by month",
            "Insurance and patient payments received by posting month, oldest first.",
            GroupedSource.Payments, [Month], PaymentMeasures, "CONVERT(char(7), {date}, 120)", true, "payments"),
        new(ClaimVolumeByPractice, "Claim volume by practice",
            "Unique claims, claim lines and charges by practice for a date-of-service period.",
            GroupedSource.ClaimVolume, [Practice], ClaimMeasures,
            "COUNT(DISTINCT Claim_No) DESC, Practice_Code", false, "claims"),
        new(ClaimVolumeByMonth, "Claim volume by month",
            "Unique claims, claim lines and charges by month of service, oldest first.",
            GroupedSource.ClaimVolume, [Month], ClaimMeasures, "CONVERT(char(7), {date}, 120)", true, "claims"),
        new(DenialsByPayer, "Denials and rejections by payer",
            "Claims with a claim-status entry in the period, by payer and status. Counts claims, never dollars.",
            GroupedSource.Denials,
            [new("payerId", "Payer ID", "Ins_Payer_Id"), new("payerName", "Payer", "Ins_Payer_Name"), Status], DenialMeasures,
            "COUNT(DISTINCT Claim_No) DESC, Ins_Payer_Id, Claim_Status", false, "denials"),
        new(DenialsByMonth, "Denials and rejections by month",
            "Claims with a claim-status entry by month of the status date and status, oldest first. Counts claims, never dollars.",
            GroupedSource.Denials, [Month, Status], DenialMeasures, "CONVERT(char(7), {date}, 120), Claim_Status", true, "denials"),
    ];

    public static GroupedSpec? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    public static IReadOnlyList<ReportColumn> Columns(GroupedSpec s)
        => [.. s.Groups.Select(g => new ReportColumn(g.Name, g.Label, ColumnType.Text)),
            .. s.Measures.Select(m => new ReportColumn(m.Name, m.Label, m.Type))];

    // The report definitions for the catalogue. Caveats are those of the parent report plus a note on grouping.
    public static IReadOnlyList<ReportDefinition> Definitions(
        IReadOnlyDictionary<string, IReadOnlyList<string>> caveatsByKey, string owner, IReadOnlyList<string> roles,
        IReadOnlyDictionary<GroupedSource, (string Source, string Basis)> sources)
        => All.Select(s => new ReportDefinition(
            s.Id, s.Name, s.Purpose, owner, "0.1-draft", "Draft",
            sources[s.Source].Source, [sources[s.Source].Basis], sources[s.Source].Basis,
            s.Source switch
            {
                GroupedSource.Payments => ["practiceCodes", "payerId", "providerId", "topN"],
                GroupedSource.ClaimVolume => ["practiceCodes", "providerId", "topN"],
                _ => ["practiceCodes", "payerId", "topN"],
            },
            Columns(s),
            [.. caveatsByKey[s.CaveatKey],
             s.Chronological
                 ? "Grouped by month, oldest first. A partial first or last month is shown as is."
                 : "Rows are ranked by the first measure; topN limits how many groups are shown, so the listed rows may not add up to the whole."],
            roles)).ToList();
}
