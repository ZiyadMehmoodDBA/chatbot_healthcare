-- Reporting view for: denial-analysis.
-- Source: dbo.FactPayerRejectionReason (claim level, no declared key; reloaded from the source on each sync).
-- Counts claims only: the warehouse has no denied-amount column.
-- Claim_Status_Reason, Procedure_Code and Diagnosis (varchar(max) free text) are left out on purpose.
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_Denials AS
SELECT
    Claim_No,
    Practice_Code,
    Ins_Payer_Id,
    Ins_Payer_Name,
    Claim_Status,
    Status_Reason,
    Claim_Status_Date
FROM dbo.FactPayerRejectionReason;
GO
