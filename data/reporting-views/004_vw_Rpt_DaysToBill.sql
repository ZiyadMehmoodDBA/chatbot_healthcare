-- Reporting view for: days-to-bill.
-- Source: dbo.FactDaysToBill (no declared key; reloaded from the source on each sync).
-- Do not use DOSAge / DOEAge: they are ages at load time and go stale. Days are computed from the dates.
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_DaysToBill AS
SELECT
    Claim_No,
    Practice_Code,
    Ins_Payer_Id,
    Ins_Payer_Name,
    DOS,
    First_Submission_Date
FROM dbo.FactDaysToBill;
GO
