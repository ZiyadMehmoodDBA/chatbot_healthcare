-- Reporting views for: submissions-by-type/month, resubmissions-by-payer/month, payment-remarks-by-code.
-- Sources: dbo.FactGlobalKPIClaimSubmissionsHistory, dbo.FactClaimResubmission, dbo.MAX_GlobalKPIClaimPaymentDenial.
-- Keys, dates, payer and codes only. No patient name, account, DOB or free-text columns (Combined_Remarks holds codes).
-- Grant the chatbot's read-only login SELECT on these views only.
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_Submissions AS
SELECT Practice_Code, Claim_No, Submission_Type, Ins_Payer_ID, Submission_Date_Time, Claim_Status
FROM dbo.FactGlobalKPIClaimSubmissionsHistory;
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_Resubmissions AS
SELECT Practice_Code, Claim_No, Ins_Payer_Id, Ins_Payer_Name, Submission_Date, Submission_Type, RowNumDateOfSubmission
FROM dbo.FactClaimResubmission;
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_PaymentRemarks AS
SELECT practice_code AS Practice_Code, claim_no AS Claim_No, Ins_Payer_Id, DOP, Combined_Remarks
FROM dbo.MAX_GlobalKPIClaimPaymentDenial;
GO
