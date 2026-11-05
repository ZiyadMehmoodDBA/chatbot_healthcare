-- Reporting view for: charges-payments-by-practice, charges-payments-by-month.
-- Source: dbo.FactClaimCount (one row per claim line; grain unconfirmed, no declared primary key).
-- Exposes amounts and keys only. No patient name, account, DOB or free-text columns.
-- Grant the chatbot's read-only login SELECT on this view only.
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_ClaimFinancials AS
SELECT
    Claim_No,
    Practice_Code,
    Ins_Payer_Id,
    Rendering_Provider_ID,
    DOS,
    Charge_Amount,
    Amount_Paid,
    Amount_Adjusted,
    Withheld_Amount
FROM dbo.FactClaimCount;
GO
