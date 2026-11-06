/*
  Run on the MedcareDW database (SQL Server 2016 SP2 or later). Run as a login that can CREATE VIEW and CREATE SCHEMA.
  Creates 4 views in schema [reporting]. Changes no tables and no data. Safe to re-run (CREATE OR ALTER).
  Part 1: pre-flight (read-only). If it lists any row, stop and send the output back.
  Part 2: the four views.
  Part 3: checks (read-only). Send all result sets back.
  Set the database in the dropdown, or uncomment the next line:
*/
-- USE [MedcareDW_Dev];
SET NOCOUNT ON;
GO

/* ===== Part 1: pre-flight. Expect ZERO rows ===== */
;WITH needed(tbl, col) AS (
    SELECT * FROM (VALUES
      ('FactClaimCount','Claim_No'),('FactClaimCount','Practice_Code'),('FactClaimCount','Rendering_Provider_ID'),
      ('FactClaimCount','Rendering_Provider'),('FactClaimCount','DOS'),('FactClaimCount','Charge_Amount'),
      ('FactDaysToBill','Claim_No'),('FactDaysToBill','Practice_Code'),('FactDaysToBill','Ins_Payer_Id'),
      ('FactDaysToBill','Ins_Payer_Name'),('FactDaysToBill','DOS'),('FactDaysToBill','First_Submission_Date'),
      ('FactPayerRejectionReason','Claim_No'),('FactPayerRejectionReason','Practice_Code'),
      ('FactPayerRejectionReason','Ins_Payer_Id'),('FactPayerRejectionReason','Ins_Payer_Name'),
      ('FactPayerRejectionReason','Claim_Status'),('FactPayerRejectionReason','Status_Reason'),
      ('FactPayerRejectionReason','Claim_Status_Date'),
      ('FactCPT_KPI','Patient_Account'),('FactCPT_KPI','Practice_Code'),('FactCPT_KPI','Claim_No'),
      ('FactCPT_KPI','Sequence_Number'),('FactCPT_KPI','DOS'),('FactCPT_KPI','CPT'),('FactCPT_KPI','Billed_Date'),
      ('FactCPT_KPI','Billing_Status'),('FactCPT_KPI','Charge_Amount'),('FactCPT_KPI','Insurance_Payments'),
      ('FactCPT_KPI','Patient_Payments'),('FactCPT_KPI','Contractual_Adjustments'),('FactCPT_KPI','Manual_Adjustments'),
      ('FactCPT_KPI','Patient_Responsibility'),('FactCPT_KPI','Patient_Balance')
    ) v(tbl, col)
)
SELECT n.tbl AS missing_table_or_column_table, n.col AS missing_column
FROM needed n
WHERE NOT EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(QUOTENAME('dbo') + '.' + QUOTENAME(n.tbl)) AND c.name = n.col);
GO

/* ===== Part 2: views ===== */
IF SCHEMA_ID(N'reporting') IS NULL EXEC(N'CREATE SCHEMA reporting');
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_ClaimVolume AS
SELECT Claim_No, Practice_Code, Rendering_Provider_ID, Rendering_Provider, DOS, Charge_Amount
FROM dbo.FactClaimCount;
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_DaysToBill AS
SELECT Claim_No, Practice_Code, Ins_Payer_Id, Ins_Payer_Name, DOS, First_Submission_Date
FROM dbo.FactDaysToBill;
GO
CREATE OR ALTER VIEW reporting.vw_Rpt_Denials AS
SELECT Claim_No, Practice_Code, Ins_Payer_Id, Ins_Payer_Name, Claim_Status, Status_Reason, Claim_Status_Date
FROM dbo.FactPayerRejectionReason;
GO
-- Patient-level: only Patient_Account is exposed as an identifier. Grant SELECT to a separate, tightly controlled login.
CREATE OR ALTER VIEW reporting.vw_Rpt_PatientBilling AS
SELECT Patient_Account, Practice_Code, Claim_No, Sequence_Number, DOS, CPT, Billed_Date, Billing_Status,
       Charge_Amount, Insurance_Payments, Patient_Payments, Contractual_Adjustments, Manual_Adjustments,
       Patient_Responsibility, Patient_Balance
FROM dbo.FactCPT_KPI;
GO

/* ===== Part 3: checks (read-only). Send every result set back ===== */

-- 3.1 Views exist (expect 4 rows)
SELECT name AS view_name, create_date FROM sys.views
WHERE schema_id = SCHEMA_ID('reporting') AND name IN
 ('vw_Rpt_ClaimVolume','vw_Rpt_DaysToBill','vw_Rpt_Denials','vw_Rpt_PatientBilling');

-- 3.2 Row counts and date ranges. All zero means the dev database is empty: point us at the populated database.
SELECT 'ClaimVolume' AS v, COUNT(*) AS row_count, COUNT(DISTINCT Claim_No) AS claims, MIN(DOS) AS min_date, MAX(DOS) AS max_date FROM reporting.vw_Rpt_ClaimVolume
UNION ALL SELECT 'DaysToBill', COUNT(*), COUNT(DISTINCT Claim_No), MIN(First_Submission_Date), MAX(First_Submission_Date) FROM reporting.vw_Rpt_DaysToBill
UNION ALL SELECT 'Denials', COUNT(*), COUNT(DISTINCT Claim_No), MIN(Claim_Status_Date), MAX(Claim_Status_Date) FROM reporting.vw_Rpt_Denials
UNION ALL SELECT 'PatientBilling', COUNT(*), COUNT(DISTINCT Claim_No), MIN(DOS), MAX(DOS) FROM reporting.vw_Rpt_PatientBilling;

-- 3.3 Grain and duplicates. Rows per claim above 1 is expected for line-level tables; what matters is the numbers.
SELECT 'ClaimVolume' AS v, COUNT(*) AS rows_total, COUNT(DISTINCT Claim_No) AS distinct_claims,
       SUM(CASE WHEN Claim_No IS NULL THEN 1 ELSE 0 END) AS null_claim_no FROM reporting.vw_Rpt_ClaimVolume
UNION ALL SELECT 'DaysToBill', COUNT(*), COUNT(DISTINCT Claim_No), SUM(CASE WHEN Claim_No IS NULL THEN 1 ELSE 0 END) FROM reporting.vw_Rpt_DaysToBill
UNION ALL SELECT 'Denials', COUNT(*), COUNT(DISTINCT Claim_No), SUM(CASE WHEN Claim_No IS NULL THEN 1 ELSE 0 END) FROM reporting.vw_Rpt_Denials;

-- 3.4 Days to bill sanity: missing dates and negative gaps are excluded by the report; how many are there?
SELECT COUNT(*) AS rows_total,
       SUM(CASE WHEN DOS IS NULL OR First_Submission_Date IS NULL THEN 1 ELSE 0 END) AS missing_dates,
       SUM(CASE WHEN First_Submission_Date < DOS THEN 1 ELSE 0 END) AS negative_gap,
       AVG(CAST(DATEDIFF(DAY, DOS, First_Submission_Date) AS float)) AS avg_days_to_bill
FROM reporting.vw_Rpt_DaysToBill;

-- 3.5 Denial statuses: which values exist (the owner decides which count as a denial)
SELECT TOP (50) Claim_Status, COUNT(*) AS rows_n, COUNT(DISTINCT Claim_No) AS claims
FROM reporting.vw_Rpt_Denials GROUP BY Claim_Status ORDER BY COUNT(*) DESC;

-- 3.6 Patient view: duplicates on the claim line key (expect 0 rows), and any reported account count only (no values)
SELECT TOP (20) Claim_No, Sequence_Number, COUNT(*) AS copies
FROM reporting.vw_Rpt_PatientBilling GROUP BY Claim_No, Sequence_Number HAVING COUNT(*) > 1 ORDER BY COUNT(*) DESC;
SELECT COUNT(DISTINCT Patient_Account) AS distinct_accounts,
       SUM(CASE WHEN Patient_Account IS NULL THEN 1 ELSE 0 END) AS null_account_rows
FROM reporting.vw_Rpt_PatientBilling;

-- 3.7 Practice codes present (the chatbot scopes users by these)
SELECT TOP (50) Practice_Code, COUNT(*) AS claim_rows FROM reporting.vw_Rpt_ClaimVolume GROUP BY Practice_Code ORDER BY COUNT(*) DESC;

-- 3.8 Existing views from earlier steps still there (expect 2 rows)
SELECT name FROM sys.views WHERE schema_id = SCHEMA_ID('reporting') AND name IN ('vw_Rpt_Payments','vw_Rpt_InsuranceAr');

/*
  After the checks, grant the chatbot's read-only login access. Replace [chatbot_reader] with the real login's database user:
    GRANT SELECT ON reporting.vw_Rpt_ClaimVolume, reporting.vw_Rpt_DaysToBill, reporting.vw_Rpt_Denials TO [chatbot_reader];
  Patient view: a SEPARATE login, not the general reader.
    GRANT SELECT ON reporting.vw_Rpt_PatientBilling TO [chatbot_patient_reader];
  Do not send account numbers, names or any row values back in chat: counts and dates are enough.
*/
