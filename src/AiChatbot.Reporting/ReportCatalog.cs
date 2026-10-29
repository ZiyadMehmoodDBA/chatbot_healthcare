namespace AiChatbot.Reporting;

// First three reports of release 1. Definitions are DRAFT until the RCM business owner signs the formulas
// (docs/Phase0_Data_Map.md). The ApprovalStatus is returned with every result so a reader can see it.
public static class ReportCatalog
{
    public const string InsuranceVsPatient = "insurance-vs-patient-payments";
    public const string PaymentsByPayer = "payments-by-payer";
    public const string InsuranceAging = "insurance-ar-aging";

    public const string Posting = "posting";
    public const string Deposit = "deposit";
    public const string Billed = "billed";

    private static readonly string[] AllRoles = [Roles.BillingStaff, Roles.RcmManager, Roles.Analyst];
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
    ];

    public static ReportDefinition? Find(string? id)
        => All.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
}
