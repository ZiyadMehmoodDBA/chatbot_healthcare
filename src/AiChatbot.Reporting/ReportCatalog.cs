namespace AiChatbot.Reporting;

// First three reports of release 1. Definitions are DRAFT until the RCM business owner signs the formulas
// (docs/Phase0_Data_Map.md). The ApprovalStatus is returned with every result so a reader can see it.
public static class ReportCatalog
{
    public const string InsuranceVsPatient = "insurance-vs-patient-payments";
    public const string PaymentsByPayer = "payments-by-payer";
    public const string InsuranceAging = "insurance-ar-aging";
    public const string ClaimVolume = "claim-volume";
    public const string DaysToBill = "days-to-bill";
    public const string Denials = "denial-analysis";
    public const string PatientBilling = "patient-billing-balance";

    public const string Posting = "posting";
    public const string Deposit = "deposit";
    public const string Billed = "billed";
    public const string ServiceDate = "dos";
    public const string Submitted = "submitted";
    public const string StatusDate = "status";
    public const string Current = "current";

    private static readonly string[] AllRoles = [Roles.BillingStaff, Roles.RcmManager, Roles.Analyst];
    // Access matrix (docs/Data_Maps.md section 3): patient-level lookups are not for analysts
    private static readonly string[] PatientRoles = [Roles.BillingStaff, Roles.RcmManager];
    private const string PaymentSource = "dbo.FACT_CC_Claims_Payment_Count via reporting view";
    private const string Owner = "RCM business owner (to be named)";

    // Verified against MedcareDW_Dev on 2026-10-29 (read-only checks): InsurancePayments + PatientPayments equals
    // Total_Payment on 1,277,711 of 1,277,736 rows, while the InsuranceAmountPaid/PatientAmountPaid pair matches on
    // only 325,048 rows. Posting_Date is never NULL. Deposit_Date is NULL on 46% of rows and holds 1900-01-01.
    private static readonly string[] PaymentCaveats =
    [
        "Draft definition: not yet approved by the RCM business owner.",
        "Payments are InsurancePayments and PatientPayments (they reconcile to Total_Payment on 99.998% of rows). The InsuranceAmountPaid/PatientAmountPaid pair is not used.",
        "Only the posting date is offered: Deposit_Date is empty on about 46% of rows and uses 1900-01-01 as a placeholder.",
        "Negative payments (recoupments and refunds) are included in the totals.",
        "The source holds one row per payment posting on a claim line; a claim can appear on several rows. Unique claims are counted by Claim_No. Eight exact duplicate rows exist in about 1.3 million.",
    ];

    public static readonly IReadOnlyList<ReportDefinition> All =
    [
        new(InsuranceVsPatient,
            "Insurance vs. patient payments",
            "How much was collected from insurance versus patients in the period, and each share of the combined total.",
            Owner, "0.1-draft", "Draft", PaymentSource,
            [Posting], Posting,
            ["practiceCodes", "payerId", "providerId"],
            [
                new("claimCount", "Unique claims", ColumnType.Integer),
                new("recordCount", "Source records", ColumnType.Integer),
                new("insurancePayments", "Insurance payments", ColumnType.Money),
                new("patientPayments", "Patient payments", ColumnType.Money),
                new("combinedPayments", "Combined payments", ColumnType.Money),
                new("difference", "Insurance minus patient", ColumnType.Money),
                new("insurancePct", "Insurance %", ColumnType.Percent),
                new("patientPct", "Patient %", ColumnType.Percent),
            ],
            PaymentCaveats, AllRoles),

        new(PaymentsByPayer,
            "Payments by payer",
            "Payers ranked by combined payments received in the period.",
            Owner, "0.1-draft", "Draft", PaymentSource,
            [Posting], Posting,
            ["practiceCodes", "providerId", "topN"],
            [
                new("payerId", "Payer ID", ColumnType.Text),
                new("payerName", "Payer", ColumnType.Text),
                new("claimCount", "Unique claims", ColumnType.Integer),
                new("insurancePayments", "Insurance payments", ColumnType.Money),
                new("patientPayments", "Patient payments", ColumnType.Money),
                new("combinedPayments", "Combined payments", ColumnType.Money),
            ],
            [.. PaymentCaveats, "Patient payments are shown against the payer on the same row; confirm that attribution is meaningful."],
            AllRoles),

        new(InsuranceAging,
            "Insurance A/R aging",
            "Open insurance balance by payer and age since the billed date.",
            Owner, "0.1-draft", "Draft", "dbo.FactDaysARbyPayer via reporting view",
            [Billed], Billed,
            ["practiceCodes", "payerId", "topN", "asOfDate"],
            [
                new("payerId", "Payer ID", ColumnType.Text),
                new("payerName", "Payer", ColumnType.Text),
                new("claimCount", "Unique claims", ColumnType.Integer),
                new("age0to30", "0-30 days", ColumnType.Money),
                new("age31to60", "31-60 days", ColumnType.Money),
                new("age61to90", "61-90 days", ColumnType.Money),
                new("age91to120", "91-120 days", ColumnType.Money),
                new("age120plus", "Over 120 days", ColumnType.Money),
                new("noBilledDate", "No billed date", ColumnType.Money),
                new("totalBalance", "Total open balance", ColumnType.Money),
            ],
            [
                "Draft definition: not yet approved by the RCM business owner.",
                "Balances are the current load of the source table; there is no historical snapshot, so a past as-of date does not reproduce past balances.",
                "Only rows with a positive Insurance_Balance are included; credits (negative balances) are excluded.",
                "Checked: among positive-balance rows there are no duplicate rows and no claim line appears twice. Duplicates exist only on zero or empty balances, which are excluded.",
                "Age is days from Billed_Date to the as-of date; rows billed after the as-of date are excluded.",
                "Bucket edges (0-30, 31-60, 61-90, 91-120, over 120) are a proposal pending owner approval.",
            ],
            AllRoles),

        new(ClaimVolume,
            "Claim volume by practice and provider",
            "Unique claims and charges by practice and rendering provider for a date-of-service period.",
            Owner, "0.1-draft", "Draft", "dbo.FactClaimCount via reporting view",
            [ServiceDate], ServiceDate,
            ["practiceCodes", "providerId", "topN"],
            [
                new("practiceCode", "Practice", ColumnType.Text),
                new("providerId", "Provider ID", ColumnType.Text),
                new("providerName", "Rendering provider", ColumnType.Text),
                new("claimCount", "Unique claims", ColumnType.Integer),
                new("lineCount", "Claim lines", ColumnType.Integer),
                new("charges", "Charges", ColumnType.Money),
            ],
            [
                "Draft definition: not yet approved by the RCM business owner.",
                "Date basis is date of service (DOS). Claims with no DOS are not counted.",
                "FactClaimCount holds one row per claim line; its grain and duplicates are not verified. Unique claims are counted by Claim_No; charges add up every line.",
                "FactClaimCount has no declared key and is reloaded from the source system on each sync, so it keeps no history.",
                "Provider names come from the warehouse as written; one provider id can appear with more than one spelling.",
            ],
            AllRoles),

        new(DaysToBill,
            "Days to bill",
            "Average and longest time from date of service to first submission, by payer, for claims first submitted in the period.",
            Owner, "0.1-draft", "Draft", "dbo.FactDaysToBill via reporting view",
            [Submitted], Submitted,
            ["practiceCodes", "payerId", "topN"],
            [
                new("payerId", "Payer ID", ColumnType.Text),
                new("payerName", "Payer", ColumnType.Text),
                new("claimCount", "Unique claims", ColumnType.Integer),
                new("avgDays", "Average days to bill", ColumnType.Decimal),
                new("maxDays", "Longest (days)", ColumnType.Integer),
            ],
            [
                "Draft definition: not yet approved by the RCM business owner.",
                "Days to bill = first submission date minus date of service (DOS). Claims with a missing DOS or submission date, or a negative result, are excluded.",
                "The first row is the overall figure across all payers; the rows below are the largest payers by claim count.",
                "FactDaysToBill is reloaded from the source on each sync and has no declared key; whether it holds one row per claim is not verified.",
                "Date of service rather than date of entry is the start event; the owner has not confirmed that choice.",
            ],
            AllRoles),

        new(Denials,
            "Denial and rejection analysis",
            "Claims with a denial or rejection status entry in the period, by status and reason.",
            Owner, "0.1-draft", "Draft", "dbo.FactPayerRejectionReason via reporting view",
            [StatusDate], StatusDate,
            ["practiceCodes", "payerId", "topN"],
            [
                new("status", "Status", ColumnType.Text),
                new("reason", "Reason", ColumnType.Text),
                new("claimCount", "Unique claims", ColumnType.Integer),
                new("recordCount", "Status records", ColumnType.Integer),
                new("payerCount", "Payers", ColumnType.Integer),
            ],
            [
                "Draft definition: not yet approved by the RCM business owner.",
                "This counts claims, not dollars: the warehouse has no denied-amount column, so no denied amount is reported.",
                "Date basis is the claim status date. Which status values mean denial versus rejection is not documented, so every status record is included and shown by its status.",
                "The first row is the overall count across all statuses and reasons. A claim can appear under more than one reason, so reason rows do not add up to the overall count.",
                "Free-text reason detail is not shown.",
            ],
            AllRoles),

        new(PatientBilling,
            "Patient billing balance",
            "Charges, payments, adjustments and the patient balance on one patient account, by claim line.",
            Owner, "0.1-draft", "Draft", "dbo.FactCPT_KPI via reporting view",
            [Current], Current,
            ["practiceCodes", "accountNumber"],
            [
                new("practiceCode", "Practice", ColumnType.Text),
                new("claimNo", "Claim", ColumnType.Text),
                new("line", "Line", ColumnType.Integer),
                new("dos", "Date of service", ColumnType.Text),
                new("cpt", "CPT", ColumnType.Text),
                new("billedDate", "Billed", ColumnType.Text),
                new("status", "Billing status", ColumnType.Text),
                new("charge", "Charge", ColumnType.Money),
                new("insurancePaid", "Insurance paid", ColumnType.Money),
                new("patientPaid", "Patient paid", ColumnType.Money),
                new("adjustments", "Adjustments", ColumnType.Money),
                new("patientResponsibility", "Patient responsibility", ColumnType.Money),
                new("patientBalance", "Patient balance", ColumnType.Money),
            ],
            [
                "Draft definition: this is a patient billing balance from the reporting warehouse, not a generated patient statement. Statement number, delivery status and statement dates are not available.",
                "Only the practices assigned to your account are searched. An account with no lines in them looks the same as one that does not exist.",
                "Adjustments are contractual plus manual adjustments. Negative balances are credits and are included in the totals.",
                "Whether patient responsibility less patient payments equals the patient balance on every line is not yet verified against the billing system.",
                "Balances are the current load of the source table; past balances cannot be reproduced.",
            ],
            PatientRoles, PatientLevel: true),
    ];

    // Safe to write to logs and the audit table: a catalog id, or "unknown" for anything the caller made up
    public static string AuditId(string? id) => Find(id)?.Id ?? "unknown";

    public static ReportDefinition? Find(string? id)
        => All.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
}
