-- Reporting view for: insurance-ar-aging.
-- Source: dbo.FactDaysARbyPayer (current-load balances; no historical snapshot).
-- Do not use dbo.FactAccountReceivable here: Balance is varchar and Due_Amount is float.
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_InsuranceAr AS
SELECT
    Claim_No,
    Practice_Code,
    Ins_Payer_Id,
    Ins_Payer_Name,
    Billed_Date,
    Insurance_Balance
FROM dbo.FactDaysARbyPayer;
GO
