-- Reporting view for: insurance-vs-patient-payments, payments-by-payer.
-- Source: dbo.FACT_CC_Claims_Payment_Count (grain unconfirmed; no declared primary key).
-- Exposes only what the reports need. No patient name, account, DOB or free-text columns.
-- Grant the chatbot's read-only login SELECT on this view only.
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_Payments AS
SELECT
    Claim_No,
    Practice_Code,
    Payer_Id,
    Payer_Name,
    Rendering_Provider_ID,
    Posting_Date,
    Deposit_Date,
    InsurancePayments,
    PatientPayments
FROM dbo.FACT_CC_Claims_Payment_Count;
GO
