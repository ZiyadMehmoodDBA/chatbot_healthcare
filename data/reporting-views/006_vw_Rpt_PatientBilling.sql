-- Reporting view for: patient-billing-balance (chat lookup of one patient account).
-- Source: dbo.FactCPT_KPI (charge-line master candidate; has a primary key). Claim line grain: Claim_No + Sequence_Number.
-- Patient_Account is the lookup key and is the only patient identifier exposed. NOT exposed: patient name, DOB, phones,
-- subscriber or policy numbers, accession number, diagnosis, notes.
-- Grant SELECT to a separate, tightly controlled login: this is patient-level data. See docs/Data_Maps.md section 3.
-- Statement number, delivery status and statement dates do not exist in the warehouse: this is a balance, not a statement.
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_PatientBilling AS
SELECT
    Patient_Account,
    Practice_Code,
    Claim_No,
    Sequence_Number,
    DOS,
    CPT,
    Billed_Date,
    Billing_Status,
    Charge_Amount,
    Insurance_Payments,
    Patient_Payments,
    Contractual_Adjustments,
    Manual_Adjustments,
    Patient_Responsibility,
    Patient_Balance
FROM dbo.FactCPT_KPI;
GO
