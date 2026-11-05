# MedcareDW_Dev Data Dictionary (refactored from live metadata)

Source: `sys.*` catalog views on `MedcareDW_Dev` (server <dev-dw-host>, SQL Server 2016 SP2-CU17), read 2026-11-04. Supersedes the DDL-parsed `SQL_Server_Data_Dictionary.md` for structure. Business meaning is still inferred from names unless marked **verified**.

## 1. Verified facts about the database

- **43 tables, 851 columns**, all in `dbo`. A `Staging` schema exists but is empty. **0 views.** **1 stored procedure.** No extended properties (no in-DB documentation).
- **Schema only: 0 rows in every table.** Row counts, grain, distinct keys and amount reconciliation cannot be verified until data is loaded.
- **9 foreign keys**, all from `FactCPT_KPI` (6), `FactFinancialClass` (1), `DimFacilitySalesRep` (1) and `DimInsuranceAddresses` (1). No FK links any other fact table, so the relationship catalog must be curated by hand.
- Primary keys exist on 23 of 43 tables; the other 20 (mostly facts) have none, so the schema does not prevent duplicate rows.
- **SQL Server 2016 SP2:** no `STRING_AGG`; `CREATE OR ALTER` is available (SP1+). Row-Level Security is available.
- **Lineage (verified from the only proc):** `dbo.Max_SyncGlobalKPIDataFromLive_bkp_Cursor_Based` does `TRUNCATE` + reload of `FactGlobalKPICPTClaimData`, `FactGlobalKPIClaimData` and `FactGlobalKPIClaimSubmissionsHistory` from `[<server>].[Medcare_DB].[dbo].View_MAX_Global_KPI_*` across a list of secondary servers. So the source is a **multi-server live system**. These three tables keep no history; history for the other tables is unverified (see section 6A, which also covers the other supplied procs). Implications: (a) "A/R at two reporting dates" needs a snapshot mechanism not yet confirmed; (b) `Practice_Code` uniqueness across source servers is unproven; (c) freshness equals the last sync.
- **Access:** the supplied login `<exploration-login>` is a member of `db_owner`, so it is not read-only. The application must use a separate `db_datareader`-only login (plus RLS).

## 2. Table index

| Table | Kind | Cols | PK | FKs out | Notes |
|---|---|---:|---|---:|---|
| `DimBillingStatuses` | Dimension | 5 | BillingStatusID | 0 |  |
| `DimClaimInsurance` | Dimension | 11 | DimClaimInsuranceID | 0 |  |
| `DimCPT` | Dimension | 10 | CPTCode | 0 |  |
| `DimDates` | Dimension | 13 | - | 0 |  |
| `DimDiagnosis` | Dimension | 9 | DiagnosisCode | 0 |  |
| `DimFacilities` | Dimension | 14 | FacilityCode | 0 |  |
| `DimFacilitySalesRep` | Dimension | 19 | Practice_Facilities_Sale_Reps_Id | 1 |  |
| `DimFinancialClass` | Dimension | 8 | FinancialClassID | 0 |  |
| `DimInsFinancialClass` | Dimension | 8 | DimInsFinancialClass | 0 |  |
| `DimInsuranceAddresses` | Dimension | 13 | InsAddressID | 1 |  |
| `DimInsurances` | Dimension | 6 | InsuranceID | 0 |  |
| `DimLocations` | Dimension | 14 | LocationCode | 0 |  |
| `DimModifiers` | Dimension | 10 | ModifierID | 0 |  |
| `DimPatients` | Dimension | 46 | PatientAccount | 0 |  |
| `DimPracticeBillingProviders` | Dimension | 4 | ID | 0 |  |
| `DimPracticePayers` | Dimension | 19 | PracticePayerID | 0 |  |
| `DimPracticePayers_Filter` | Dimension | 4 | - | 0 |  |
| `DimPractices` | Dimension | 15 | PracticeCode | 0 |  |
| `DimProviders` | Dimension | 14 | ProviderCode | 0 |  |
| `DimReportClaimInsurance` | Dimension | 21 | Claim_Insurance_Id | 0 |  |
| `DimReportClaimPhysicians` | Dimension | 9 | Physician_Id | 0 |  |
| `DimSalesRep` | Dimension | 16 | Practice_sales_Rep_Id | 0 |  |
| `FACT_CC_Claims_Payment_Count` | Fact/report | 33 | - | 0 | payments; overlaps FactReportClaimPaymentDetails |
| `Fact_DaysAR` | Fact/report | 8 | - | 0 |  |
| `Fact_Interg` | Fact/report | 26 | - | 0 | near-duplicate of Fact_Interg_Data (column naming differs) |
| `Fact_Interg_Data` | Fact/report | 26 | - | 0 | near-duplicate of Fact_Interg |
| `FactAccountReceivable` | Fact/report | 13 | - | 0 | Balance varchar, Due_Amount float: do not aggregate |
| `FactClaimCount` | Fact/report | 32 | - | 0 |  |
| `FactClaimCptReport` | Fact/report | 48 | - | 0 | cleanest CPT measure set |
| `FactClaimInterestReport` | Fact/report | 22 | - | 0 |  |
| `FactClaimResubmission` | Fact/report | 14 | - | 0 |  |
| `FactClaimsSubmissionAccountReceivable` | Fact/report | 5 | - | 0 |  |
| `FactCPT_KPI` | Fact/report | 99 | FactCPT_KPI_ID | 6 | 99 cols; only fact with FKs |
| `FactDaysARbyPayer` | Fact/report | 14 | - | 0 |  |
| `FactDaysToBill` | Fact/report | 14 | - | 0 |  |
| `FactFacilitySalesRep` | Fact/report | 19 | - | 0 |  |
| `FactFinancialClass` | Fact/report | 16 | FactFinancialClassID | 1 |  |
| `FactGlobalKPIClaimData` | Fact/report | 24 | - | 0 | loaded by sync proc |
| `FactGlobalKPIClaimSubmissionsHistory` | Fact/report | 9 | - | 0 | loaded by sync proc |
| `FactGlobalKPICPTClaimData` | Fact/report | 29 | - | 0 | loaded by sync proc |
| `FactPayerRejectionReason` | Fact/report | 19 | - | 0 | free-text reason |
| `FactReportClaimPaymentDetails` | Fact/report | 82 | Claim_Payment_Id | 0 | payment lines, richest; has PK |
| `MAX_GlobalKPIClaimPaymentDenial` | Fact/report | 11 | - | 0 | no denied-amount column |

## 3. Foreign keys (declared)

| Constraint | From | To |
|---|---|---|
| FK_FactCPT_KPI_DimCPT | `FactCPT_KPI.CPT` | `DimCPT.CPTCode` |
| FK_DimFacilitySalesRep_DimFacilities | `DimFacilitySalesRep.Practice_Facility_Id` | `DimFacilities.FacilityCode` |
| FK_FactFinancialClass_DimFinancialClass | `FactFinancialClass.Financial_Class_ID` | `DimFinancialClass.FinancialClassID` |
| FK_FactCPT_KPI_DimFinancialClass | `FactCPT_KPI.Financial_Class_ID` | `DimFinancialClass.FinancialClassID` |
| FK_InsuranceAddresses_DimInsuranceAddresses_DimInsurancePayerID | `DimInsuranceAddresses.InsurancePayerID` | `DimInsurances.InsurancePayerID` |
| FK_FactCPT_KPI_DimLocations | `FactCPT_KPI.Practice_Location_Id` | `DimLocations.LocationCode` |
| FK_FactCPT_KPI_DimPatients | `FactCPT_KPI.Patient_Account` | `DimPatients.PatientAccount` |
| FK_FactCPT_KPI_DimProviders | `FactCPT_KPI.Provider_Code` | `DimProviders.ProviderCode` |
| FK_FactCPT_KPI_DimSalesRep | `FactCPT_KPI.Practice_sales_Rep_Id` | `DimSalesRep.Practice_sales_Rep_Id` |

## 4. Candidate joins (inferred, NOT declared)

Same column name in several tables. A shared name is a **hypothesis**, not a certified join. Each needs a distinct-key and overlap test once data exists.

| Key column | Tables containing it | Dimension target (guess) |
|---|---|---|
| `Claim_No` | DimClaimInsurance, DimReportClaimInsurance, FACT_CC_Claims_Payment_Count, FactAccountReceivable, FactCPT_KPI, FactClaimCount, FactClaimCptReport, FactClaimInterestReport, FactClaimResubmission, FactClaimsSubmissionAccountReceivable, FactDaysARbyPayer, FactDaysToBill, FactGlobalKPICPTClaimData, FactGlobalKPIClaimData, FactGlobalKPIClaimSubmissionsHistory, FactPayerRejectionReason, FactReportClaimPaymentDetails, Fact_DaysAR, MAX_GlobalKPIClaimPaymentDenial | (no claim dimension; grain varies by table) |
| `Practice_Code` | DimClaimInsurance, DimInsFinancialClass, DimPracticeBillingProviders, DimPracticePayers, DimPracticePayers_Filter, DimReportClaimInsurance, DimSalesRep, FACT_CC_Claims_Payment_Count, FactAccountReceivable, FactCPT_KPI, FactClaimCount, FactClaimCptReport, FactClaimInterestReport, FactClaimResubmission, FactDaysARbyPayer, FactDaysToBill, FactFinancialClass, FactGlobalKPICPTClaimData, FactGlobalKPIClaimData, FactGlobalKPIClaimSubmissionsHistory, FactPayerRejectionReason, FactReportClaimPaymentDetails, Fact_DaysAR, MAX_GlobalKPIClaimPaymentDenial | DimPractices.PracticeCode |
| `PracticeCode` | DimFacilities, DimLocations, DimPatients, DimPractices, DimProviders | DimPractices.PracticeCode |
| `Ins_Payer_Id` | DimClaimInsurance, DimInsFinancialClass, DimPracticePayers_Filter, DimReportClaimInsurance, FactCPT_KPI, FactClaimCount, FactClaimCptReport, FactClaimInterestReport, FactClaimResubmission, FactDaysARbyPayer, FactDaysToBill, FactFinancialClass, FactGlobalKPIClaimSubmissionsHistory, FactPayerRejectionReason, FactReportClaimPaymentDetails, Fact_DaysAR, MAX_GlobalKPIClaimPaymentDenial | DimInsurances.InsurancePayerID |
| `Financial_Class_ID` | DimClaimInsurance, DimInsFinancialClass, DimPracticePayers, DimReportClaimInsurance, FactCPT_KPI, FactClaimCptReport, FactClaimInterestReport, FactFinancialClass, FactReportClaimPaymentDetails | DimFinancialClass.FinancialClassID |
| `Patient_Account` | DimReportClaimInsurance, FactCPT_KPI, FactClaimCount, FactReportClaimPaymentDetails, Fact_Interg_Data | DimPatients.PatientAccount |
| `Provider_Code` | DimPracticeBillingProviders, FactCPT_KPI | DimProviders.ProviderCode |
| `Practice_Location_Id` | FactCPT_KPI, FactClaimCount, FactClaimResubmission, FactDaysToBill, FactPayerRejectionReason, FactReportClaimPaymentDetails | DimLocations.LocationCode |
| `Procedure_Code` | FACT_CC_Claims_Payment_Count, FactAccountReceivable, FactClaimCount, FactClaimCptReport, FactClaimsSubmissionAccountReceivable, FactDaysARbyPayer, FactPayerRejectionReason, FactReportClaimPaymentDetails | DimCPT.CPTCode (varchar(5/20) vs varchar(50)) |
| `CPT` | FactCPT_KPI | DimCPT.CPTCode (declared FK) |
| `Payer_Id` | FACT_CC_Claims_Payment_Count | DimInsurances.InsurancePayerID? |
| `Sales_Rep_Id` | FactClaimCptReport, FactClaimInterestReport, FactReportClaimPaymentDetails | DimSalesRep? |
| `Rendering_Provider_ID` | FACT_CC_Claims_Payment_Count, FactClaimCount | DimProviders.ProviderCode? |

## 5. Money, date and PHI inventory

Role tags are heuristics from names and types: MEASURE, DATE, KEY, FLAG, `PHI?` (possible PHI: treat as restricted until classified).

### Money-like columns whose type is not `money`/`decimal` (aggregation hazards)

| Table | Column | Type |
|---|---|---|
| Fact_Interg | `Charge Responsibility` | varchar(100) |
| Fact_Interg_Data | `Charge_Responsibility` | varchar(100) |
| FactAccountReceivable | `Balance` | varchar(15) |
| FactAccountReceivable | `Charge_Reponsibility` | varchar(500) |
| FactAccountReceivable | `Due_Amount` | float |
| FactCPT_KPI | `Charge_Reponsibility` | nvarchar(100) |
| FactGlobalKPICPTClaimData | `Charge_Reponsibility` | nvarchar(100) |
| FactReportClaimPaymentDetails | `PatientPayment_Faciltiy` | varchar(300) |
| FactReportClaimPaymentDetails | `Payment_Description` | varchar(401) |
| FactReportClaimPaymentDetails | `Payment_Method` | varchar(100) |
| FactReportClaimPaymentDetails | `Payment_Source` | varchar(50) |
| FactReportClaimPaymentDetails | `Adjustment_Reason` | varchar(200) |
| FactReportClaimPaymentDetails | `Payment_Faciltiy` | varchar(300) |
| MAX_GlobalKPIClaimPaymentDenial | `Combined_Adjustments` | varchar(71) |

### Date columns per fact table (each metric must pick ONE date basis)

| Table | Date columns |
|---|---|
| FACT_CC_Claims_Payment_Count | Date_of_Service, Posting_Date, Deposit_Date, Check_Date, DOE |
| Fact_DaysAR | Billed_Date |
| Fact_Interg | DOS, Number of Days between DOS and Date Billed, First Billed Date, Last Billed Date, StartDate, EndDate |
| Fact_Interg_Data | DOS, Number_of_Days_between_DOS_and_DateBilled, First_Billed_Date, Last_Billed_Date, StartDate, EndDate |
| FactAccountReceivable | Date_of_Service |
| FactClaimCount | DOS, DOE |
| FactClaimCptReport | Created_Date, DOS, DOE, DOP, Check_Date, Billed_Date, Last_Billed_Date |
| FactClaimInterestReport | Created_Date, DOS, DOE, DOP, Check_Date, Billed_Date, Last_Billed_Date |
| FactClaimResubmission | DOS, Submission_Date, RowNumDateOfSubmission |
| FactClaimsSubmissionAccountReceivable | Date_of_Service, Created_Date |
| FactCPT_KPI | DOB, DOS, Start_DOS, End_DOS, Entered_Date, Posting_Date, Billed_Date, Last_Billed_Date |
| FactDaysARbyPayer | Billed_Date |
| FactDaysToBill | First_Submission_Date, DOS, DOE |
| FactFacilitySalesRep | Effective_From, Effective_To, Date_of_Birth, StartDate, EndDate |
| FactFinancialClass | Date_of_Entry, Date_of_Service, Check_Date |
| FactGlobalKPIClaimData | Date_of_Birth, Date_of_Service, Entered_Date, First_Billed_Date, Last_Billed_Date |
| FactGlobalKPIClaimSubmissionsHistory | Submission_Date_Time, Claim_Status_Date |
| FactGlobalKPICPTClaimData | Date_of_Birth, Date_of_Service, Entered_Date, First_Billed_Date, Last_Billed_Date |
| FactPayerRejectionReason | DOS, DOE, Submission_Date, Claim_Status_Date |
| FactReportClaimPaymentDetails | Claim_Modified_Date, DOS, Deposit_Date, Entered_Date, Posting_Date, Actual_Check_Date, Check_Date, Batch_Date, Modified_Date |
| MAX_GlobalKPIClaimPaymentDenial | DOP, Submission_Date_Time |

### Possible PHI in fact tables (must not reach aggregate responses or the LLM)

| Table | Columns |
|---|---|
| Fact_Interg | Patient Name, Acct Nbr |
| Fact_Interg_Data | Patient_Name, Patient_Account |
| FactClaimCount | Patient_Account, Patient_Name, Address_Line1, Zip |
| FactClaimCptReport | Insurance_Addresses_Id |
| FactClaimInterestReport | Insurance_Addresses_Id |
| FactCPT_KPI | Accession_No, Patient_Account, Patient, DOB, Patient_Primary_Phone, Patient_Secondary_Phone, Primary_Policy_Type, Primary_Subscriber_Number, Secondary_Policy_Type, Secondary_Subscriber_Number, Tertiary_Policy_Type, Tertiary_Subscriber_Number |
| FactDaysARbyPayer | Insurance_Addresses_Id |
| FactFacilitySalesRep | First_Name, Last_Name, Date_of_Birth, Email, Address, Zip |
| FactGlobalKPIClaimData | Accession_No, Patient_Name, Date_of_Birth, Primary_Subscriber_Number |
| FactGlobalKPICPTClaimData | Accession_No, Patient_Name, Date_of_Birth, Primary_Subscriber_Number |
| FactReportClaimPaymentDetails | Accession_No, Patient_Account, Patient |

Added by review: `Fact_Interg.[Acct Nbr]` (bigint) is the patient account number under another name, and `Fact_Interg.[Patient Name]` is the patient name; neither carries a `PHI?` tag because the names differ from the `Fact_Interg_Data` twin. A scan of all 43 tables for names containing acct, account, mrn, ssn, dob, birth or patient found no other untagged patient identifier (the remaining hits are money measures or already tagged).

### Dimensions holding PHI

`DimPatients` (names, DOB, SSN, address, phones, email, policy numbers) and `DimFacilitySalesRep` (rep DOB, address, email). Keep out of the LLM-visible catalog; expose only through permissioned patient-level views.

## 6. Gap resolution register

Each interim resolution was checked **column by column against the live schema** (2026-11-04). Nothing here is certified: the database has 0 rows, so every "validate" item below is an open test, not a result. Rule: a column name appearing relevant is not acceptance. Anything unproven is marked UNAVAILABLE rather than estimated.

Status key: **PARTIAL** = usable as an interim, labeled source. **NEEDS VALIDATION** = candidate only; the agent must not use it until the tests pass. **UNAVAILABLE** = no source in the warehouse.

### 6.0 Corrections to the gap-resolution proposal (schema does not match)

| Proposal said | Live schema says | Effect |
|---|---|---|
| `FactClaimCptReport` has `Posting_Date`, `Entered_Date` | **Neither exists.** It has `Created_Date`, `DOS`, `DOE`, `DOP`, `Check_Date`, `Billed_Date`, `Last_Billed_Date` | Charge posting date has **one** candidate source, `FactCPT_KPI.Posting_Date` |
| `FactClaimCptReport` has `Patient_Responsibility`, `Due_Amount`, `Client_Bill_Balance` | **None exist.** It has `Patient_Balance`, `Patient_Payments`, `Client_Bill_Payments`, `Paid_To_Patient`, `Responsible_Party` | Patient responsibility and client-bill balance come **only** from `FactCPT_KPI` |
| `FactCPT_KPI.Responsible_Party` | Absent. It has `Responsible_Party_Name` and `Charge_Reponsibility` (sic) | Use those names |
| `FactPayerRejectionReason` joins at `Claim_No` + `Sequence_Number` | **No `Sequence_Number`.** Claim-level, with `Procedure_Code varchar(max)` | Denials cannot be tied to a charge line |
| `Fact_Interg_Data` has `Claim_No`, `Practice_Code` | **Neither.** Keys are `Billed_Claim_Id`, `Patient_Account`; no practice column | Practice isolation cannot be enforced on this table; join path to a claim is unproven |
| `FactClaimCptReport.Patient_Payments` etc. are one source | `FactCPT_KPI` also holds `Insurance_Payments`, `Patient_Payments`, `Contractual_Adjustments`, `Manual_Adjustments`, `Charge_Amount`, both balances, `Posting_Date`, PK, and 6 declared FKs | `FactCPT_KPI` is the strongest candidate **charge-line master**; `FactClaimCptReport` is a second, overlapping copy to reconcile against |

### 6.1 Resolution matrix

| # | Gap | Interim source (verified columns) | Status | Hard limits |
|---|---|---|---|---|
| 1 | Patient statements | `FactCPT_KPI`: `Patient_Account`, `Practice_Code`, `Claim_No`, `Sequence_Number`, `Patient_Responsibility`, `Patient_Payments`, `Patient_Balance`, `Client_Bill_Balance`, `Due_Amount`, `TotalDue`, `Billed_Date`, `Last_Billed_Date`. Cross-check: `FactClaimCptReport.Patient_Balance`, `Patient_Payments`, `Client_Bill_Payments` | **PARTIAL** | Call it **Patient Billing Balance**, never "statements". UNAVAILABLE: statement generated?, statement number, delivery date, method, status, count sent |
| 2 | Denied amounts | Status/reason: `FactPayerRejectionReason` (`Claim_Status`, `Status_Reason`, `Claim_Status_Reason`, `Claim_Status_Date`). Text codes: `MAX_GlobalKPIClaimPaymentDenial` (`Combined_Remarks`, `Combined_Adjustments`). Amounts: `Fact_Interg_Data` (`Denial`, `Reason`, `Charge_Amount`, `Payment_Received`, `Adjusted_Amount`, `Balance`, `Expected_Recoverable`). Counts: `FactFinancialClass` (`TotalClaims`, `Totaldenials`). Claim balance: `FactGlobalKPIClaimData.Outstanding_Amount`. Denial snapshot with entry date and amount due: `Fact_Frozen_Denial_Data` (production only; absent in dev, see 6A.3) | **PARTIAL (counts, reasons, amount due)**; **denied dollars UNAVAILABLE** | No column holds a denied amount. Never report charge or balance as "denied amount". Allowed interim metric: **"Outstanding balance on claims with denial status"** (an exposure figure, labeled as such) |
| 3 | Payment-to-charge allocation | Payment lines `FactReportClaimPaymentDetails` (`Claim_Payment_Id`, `Claim_No`, `Sequence_Number`, `Procedure_code`, `Amount_Paid`, `Amount_Adjusted`, `Unapplied`, batch/check ids). Charge lines `FactCPT_KPI` and `FactClaimCptReport` (`Claim_No`, `Sequence_Number`; the latter also has `Claim_charges_Id`, `P_Sequence_Number`) | **NEEDS VALIDATION** | Join candidate is `(Claim_No, Sequence_Number)`; types differ (`FactCPT_KPI.Sequence_Number` is `int`, others `bigint`). If a payment line cannot be tied to exactly one charge line, report at claim level and mark charge-level **unavailable**. No proportional or estimated allocation without written business approval |
| 4 | Reversal / refund | `FactReportClaimPaymentDetails`: `Is_Rectified`, `Rectified_Entry_ID`, `Payment_Description`, `Comments`, `Amount_Paid`, `Amount_Adjusted`, `Payment_Source`, `Unapplied`. Also `FactClaimCptReport.Paid_To_Patient` (money; possible patient-refund measure, meaning unknown). No column anywhere is named reversal, refund, void or write-off | **NEEDS VALIDATION** | A negative amount is never auto-classed as refund. Classes: normal, potential reversal, potential refund, rectified, unapplied, unknown. Each row carries a confidence; unresolved rows go to an **exception** bucket and are excluded from certified totals |
| 5 | Charge posting date | `FactCPT_KPI.Posting_Date` (`date`), `Posting_date_by_month`; companions `Entered_Date`, `DOS`, `Billed_Date`, `Last_Billed_Date`. `FactClaimCptReport` has **no** posting date (`Created_Date` is a weaker candidate) | **NEEDS VALIDATION** (one candidate field) | Unproven whether `FactCPT_KPI.Posting_Date` is charge posting or last payment posting. `FACT_CC_Claims_Payment_Count.Posting_Date` and `FactReportClaimPaymentDetails.Posting_Date` are **payment** posting dates. Keep DOS, entered, posted, billed as separate date bases; never substitute DOS |

### 6.2 Validation tests to run once data is loaded (acceptance gate)

| Gap | Test | Pass condition |
|---|---|---|
| 1 | `SUM(Patient_Balance)` and `SUM(Client_Bill_Balance)` from `FactCPT_KPI` vs `FactClaimCptReport.Patient_Balance` per practice; vs billing application patient A/R report | Within agreed tolerance; differences explained |
| 1 | `Patient_Responsibility - Patient_Payments - (adjustments)` vs `Patient_Balance` | Identity holds or the rule is documented |
| 2 | Does every claim with `Claim_Status` denied/rejected appear in `FactGlobalKPIClaimData` with `Outstanding_Amount`? How many denied claims are fully paid? | Ratio reported; "denied but paid" excluded from exposure |
| 2 | Can `Fact_Interg_Data.Billed_Claim_Id` be proven equal to `Claim_No`? Is `Denial` a status or a flag? Does `Expected_Recoverable` equal a denied amount? | Yes/no recorded per question; else table stays out of certified metrics |
| 2 | `Totaldenials` grain (per practice/payer/financial class/entry date?) | Grain documented |
| 3 | Uniqueness of `(Claim_No, Sequence_Number)` in `FactCPT_KPI` and `FactClaimCptReport` | Unique, or duplicate rule documented |
| 3 | Orphans: payment lines with no matching charge line, and the reverse | 0, or listed as exceptions |
| 3 | `SUM(Amount_Paid)` by claim line vs `Insurance_Payments + Patient_Payments` | Reconciles |
| 3 | Share of payment lines with null/0 `Sequence_Number` | Reported; those rows are claim-level only |
| 4 | Count of negative `Amount_Paid`; their `Is_Rectified`, `Rectified_Entry_ID` pairing; distinct `Payment_Description` / `Payment_Source` values | Rules derived from observed values, approved by RCM owner |
| 4 | Does `Rectified_Entry_ID` point at an existing `Claim_Payment_Id` whose amount offsets? | Pairing rate reported |
| 5 | `Posting_Date` vs `Entered_Date` vs `Billed_Date` ordering; null rate; compare to source billing app charge posting timestamps | Meaning confirmed by ETL owner |

### 6.3 Planned compatibility views (built only after the tests pass, in a separate schema; the source tables stay unchanged)

| View | Built from | Gate |
|---|---|---|
| `rpt.vw_PatientBillingBalance` | `FactCPT_KPI` (PHI columns excluded) | Gap 1 tests |
| `rpt.vw_ClaimDenialAnalysis` | `FactPayerRejectionReason` + `FactGlobalKPIClaimData` (+ `Fact_Interg_Data` only if its key proves out) | Gap 2 tests |
| `rpt.vw_ClaimChargePaymentAnalysis` | `FactReportClaimPaymentDetails` + charge-line master | Gap 3 tests |
| `rpt.vw_PaymentTransactionClassification` | `FactReportClaimPaymentDetails` (+ confidence column) | Gap 4 tests |
| `rpt.vw_ChargePosting` | `FactCPT_KPI.Posting_Date` | Gap 5 tests |

SQL Server 2016: write each as `CREATE VIEW` (no `CREATE OR ALTER`). The app login stays read-only; views are created by a DBA login in a migration script.

### 6.4 Other gaps (unchanged)

| Spec need | Status |
|---|---|
| A/R at two historical dates | **UNAVAILABLE for now.** The A/R facts (`FactDaysARbyPayer`, `Fact_DaysAR`) are truncate/reload (section 6A.1). History in other tables is unverified. A nightly snapshot table would need a write-access migration and approval |
| Scope of coverage | `FactClaimCptReport` and `FactClaimInterestReport` hold only practices 21003 and 21188 (section 6A.1). Answers built on them must state that scope |
| Payer join hazard | `DimReportClaimInsurance` is one row per claim-insurance; joining on `Claim_No` multiplies rows (section 6A.2) |
| Practice / provider hierarchy | Present; only partly FK-linked |
| Payer mapping (Medicare etc.) | `DimInsurances`, `DimPracticePayers`, `DimFinancialClass.HCFA_Class`, `Insurance_Type`; Medicare rule undecided |
| Overlapping payment sources | `FACT_CC_Claims_Payment_Count`, `FactClaimCount`, `FactReportClaimPaymentDetails`, plus line totals in `FactCPT_KPI`: authoritative source undecided |

## 6A. ETL lineage and schema drift (from the supplied stored procedures)

Procedures supplied by the user on 2026-11-04 (not present in `MedcareDW_Dev`, which holds only a backup-named proc): `Max_SyncDataFromLive`, `SP_Max_BulkMaintainReportClaimPaymentDetailJob`, `SP_Max_BulkMaintainReportClaimInsuranceJob`, `SP_Max_BulkMaintainReportClaimPhysiciansJob`, `Freeze_Denial_Data`, and the in-database `Max_SyncGlobalKPIDataFromLive_bkp_Cursor_Based`. Facts below are read from that code; they describe the production DW's behavior and **must be re-confirmed against the production proc definitions**.

### 6A.1 Load behavior per table

| Target table | Source (via linked server) | Mode | Filter / note |
|---|---|---|---|
| `FactDaysToBill` | `<linked-server-b>.Medcare_DB.BI_MAX_ViewDaysToBill` | TRUNCATE + reload | none |
| `FACT_CC_Claims_Interest_Count` | `<linked-server-b>` `BI_View_CC_Claims_Interest_Count` (`SELECT *`) | TRUNCATE + reload | **table absent in dev DW** |
| `FactClaimCount` | `<linked-server-b>` `BI_View_Claims_Count` | TRUNCATE + reload | proc inserts `Created_Date`, **column absent in dev table** |
| `FACT_CC_Claims_Payment_Count` | `<linked-server-b>` `BI_View_CC_Claims_Payment_Count` | TRUNCATE + reload | dev table has extra `Outstanding_Balance`, `POS`, `Id` never loaded |
| `DimPracticePayers_Filter` | `BI_CC_Max_Practice_Payers` | TRUNCATE + reload | |
| `DimPracticeBillingProviders` | `BI_CC_MAX_Practice_Billing_Providers` | TRUNCATE + reload | |
| `FactPayerRejectionReason` | `BI_CC_MAX_ViewPayerRejectionReason` | TRUNCATE + reload | |
| `FactClaimResubmission` | `BI_MAX_CC_ViewClaimResubmission` | TRUNCATE + reload | |
| `FactDaysARbyPayer` | `BI_CC_View_Days_AR_byPayer` | TRUNCATE + reload | |
| `Fact_DaysAR` | `BI_CC_View_Days_AR` | TRUNCATE + reload | |
| `FactClaimInterestReport` | `BI_MAX_ClaimInterestReport` | TRUNCATE + reload | **only `Practice_Code IN (21003, 21188)`** |
| `FactClaimCptReport` | `BI_MAX_ClaimCptReport` and `BI_MAX_ClaimCptReportBrio` | TRUNCATE + 2 inserts | **only practice 21003 (standard view) and 21188 (Brio view, adds `PCharge_Amount`)**; `COGS` never loaded |
| `DimDates` | generated in proc | TRUNCATE + reload | 2020-01-01 to 2030-12-31; weekday-only business-day rule, no holidays |
| `FactReportClaimPaymentDetails` | `<source-replica-host>.Medcare_DB.Report_Fact_Claim_Payment_Details` | **Incremental upsert by `Row_Version` hash** (SHA2-256) | rows are never deleted; source deletions are not propagated |
| `DimReportClaimInsurance` | `<source-replica-host>` `Claim_Insurance` JOIN `Claims`, `Financial_Class`, `Insurance_addresses`, `Insurance_Payers` | Incremental upsert by hash | source filter `Deleted = 0`; rows that become deleted are never updated or removed |
| `DimReportClaimPhysicians` | `<source-replica-host>` `Physicians` joined to `Claims`, `Claims_Extended`, `Patients` | Incremental upsert by hash | only physicians referenced by a live claim/patient |
| `Fact_Frozen_Denial_Data` | `<linked-server-c>.Medcare_DB.dbo.Freeze_Denial_Data` (proc result) | TRUNCATE + reload, with a `Snapshot_Date` column | **table absent in dev DW** |
| `FactGlobalKPI*` (3 tables) | `[<server>].Medcare_DB.View_MAX_Global_KPI_*` over a server list | TRUNCATE + reload per server loop | multi-server |

### 6A.2 Consequences

1. **Scope is narrower than the schema suggests.** `FactClaimCptReport` and `FactClaimInterestReport` hold only practices **21003 and 21188**. Every metric built on them is valid for those practices only, and the assistant must say so rather than answer for "all practices".
2. **Sources span at least four servers/replicas:** `<linked-server-a>` (availability-group primary check for `<ag-primary-host>`), `<linked-server-b>`, `<linked-server-c>`, and `<source-replica-host>`. The three payment/insurance/physician incremental jobs read a different server (`<source-replica-host>`) from the truncate-reload facts (`<linked-server-b>`). **Different tables can reflect different source databases and different refresh moments**, so cross-table reconciliation (for example payments vs charge lines) can show timing differences that are not errors. This also bears on whether `Practice_Code` is unique across servers (still unproven).
3. **History is not uniform.** Only the tables identified as TRUNCATE + reload lose prior contents. The three incremental jobs keep and update rows (and never delete). The history status of the remaining tables is **unverified**. Do not promise historical A/R until a snapshot source is confirmed.
4. **Fail-open hazard:** the truncate-reload procs are not wrapped in a transaction, so a failure after a `TRUNCATE` leaves a table empty or partial. The assistant needs a freshness/completeness check (row count and last-load time) before it answers.
5. **Dirty reads at source and in the DW:** procs run `READ UNCOMMITTED` / `NOLOCK`. Loaded snapshots can be internally inconsistent, which is another reason cross-table reconciliation may not tie exactly.
6. **`DimReportClaimInsurance` is one row per claim-insurance (`Claim_Insurance_Id`), not per claim.** Joining it to claim-line facts on `Claim_No` **multiplies rows by the number of insurances (primary/secondary/tertiary)** and will double count money. It must be filtered to one insurance level or aggregated before joining.
7. **Deleted data persists** in the incremental tables (`Claim_Deleted`, `Deleted`). Metrics over `FactReportClaimPaymentDetails` must exclude `Claim_Deleted = 1` unless the metric owner decides otherwise; whether `Claim_Deleted` rows should count is an open question.
8. **Change detection weakness:** the row hash is `CONCAT(...)`, which turns NULL into an empty string, so a change from NULL to empty string or between adjacent values can be missed. Low risk, but note it when a reconciliation difference is unexplained.
9. **Payer identifiers differ by table** (`Ins_Payer_Id`, `Payer_Id`, `Ins_Assigned_Payer_Id`, `InsurancePayerID`); the latter is the assigned clearing-house payer id, a distinct concept.

### 6A.3 Denial snapshot (`Fact_Frozen_Denial_Data`) — new, but still not a denied amount

Columns written by the proc: `Snapshot_Date, Claim_No, Practice_Code, Date_of_Service, Denial_Entry_Date, TotalDue, Next_Claim_Note, Next_Followup_Date, Next_Submission_Date, Next_Posting_Date, Claim_Category_Name, Primary_Payer, Secondary_Payer, Ter_Payer`. This is the first source with a **denial entry date** and a dollar field (`TotalDue`) per denied claim, plus follow-up dates. `TotalDue` is claim **amount due**, not the denied amount, so the existing rule stands: report it only as "amount due on denied claims". `Next_Claim_Note` is free text and may contain PHI. Because the table is truncated each run, it holds one snapshot at a time. The table does not exist in the dev DW, so its types and keys are unknown.

### 6A.4 Schema drift: dev DW vs production ETL

| Item | Finding | Action needed |
|---|---|---|
| `FACT_CC_Claims_Interest_Count` | Loaded by `Max_SyncDataFromLive` (18 columns named in the proc), missing from dev | Obtain the production DDL; do not guess types |
| `Fact_Frozen_Denial_Data` | Loaded by `Freeze_Denial_Data` (14 columns), missing from dev | Obtain the production DDL |
| `FactClaimCount.Created_Date` | Proc inserts it; dev table has no such column; the proc would fail on dev | Add column in dev after confirming type from production |
| `FACT_CC_Claims_Payment_Count.Outstanding_Balance`, `.POS` | Exist in dev, never loaded by the sync proc | Determine whether another process fills them; otherwise treat as always NULL |
| `FactClaimCptReport.COGS` | Exists, never loaded | Treat as unavailable |
| `Id` identity columns | Not loaded (expected) | none |
| Procedure definitions | Only the backup-named proc exists in dev | Add production procs to source control and compare |

No change was made to the dev database: the guess-free fix is to take the production DDL. These two missing tables and the extra column are **proposed** schema changes pending that DDL and your approval.

### 6A.5 Corrections to earlier statements

- SQL Server 2016 **SP1 and later support `CREATE OR ALTER`**. The dev server is SP2, so it is available. (The earlier "no `CREATE OR ALTER`" note was wrong; `STRING_AGG` is still unavailable.)
- "The warehouse keeps no history" was too broad. See 6A.2 item 3.

## 7. Table definitions

### `DimBillingStatuses`

Dimension, 5 columns, PK: BillingStatusID

Other indexes: `UQ_DimBillingStatuses_BillingStatus` (NONCLUSTERED, unique) on BillingStatus

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `BillingStatusID` | `int` | N | Y | KEY |
| `BillingStatus` | `nvarchar(50)` | N |  |  |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimClaimInsurance`

Dimension, 11 columns, PK: DimClaimInsuranceID

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `DimClaimInsuranceID` | `bigint` | N | Y | KEY |
| `Claim_No` | `bigint` | N |  | KEY |
| `Practice_Code` | `bigint` | N |  | KEY |
| `Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Ins_Address_Id` | `bigint` | N |  | KEY,PHI? |
| `Ins_Payer_Id` | `bigint` | N |  | KEY |
| `Ins_Payer_Name` | `varchar(100)` | N |  |  |
| `Insurance_Type` | `varchar(15)` | Y |  |  |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndtDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimCPT`

Dimension, 10 columns, PK: CPTCode

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `CPTID` | `int` | N | Y | KEY |
| `CPTCode` | `varchar(50)` | N |  | KEY |
| `ShortDescription` | `varchar(2000)` | Y |  |  |
| `LongDescription` | `varchar(2000)` | Y |  |  |
| `CPTType` | `varchar(100)` | Y |  |  |
| `ActiveFrom` | `datetime` | Y |  | DATE |
| `ActiveTo` | `datetime` | Y |  | DATE |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimDates`

Dimension, 13 columns, PK: none

Other indexes: `uq_DimDates_dates` (NONCLUSTERED, unique) on Date

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `RID` | `bigint` | Y |  | KEY |
| `Date` | `datetime` | Y |  | DATE |
| `Quarter` | `int` | Y |  |  |
| `DayofYear` | `int` | Y |  |  |
| `WeekofYear` | `int` | Y |  |  |
| `Year` | `int` | Y |  |  |
| `Month` | `int` | Y |  |  |
| `Day` | `int` | Y |  |  |
| `Weekday` | `int` | Y |  |  |
| `MonthName` | `nvarchar(30)` | Y |  |  |
| `WeekdayName` | `nvarchar(30)` | Y |  |  |
| `IsBusinessDay` | `int` | N |  |  |
| `Vintage` | `varchar(6)` | Y |  |  |

### `DimDiagnosis`

Dimension, 9 columns, PK: DiagnosisCode

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `DiagnosisID` | `int` | N | Y | KEY |
| `DiagnosisCode` | `varchar(50)` | N |  | KEY |
| `Description` | `varchar(300)` | Y |  |  |
| `DiagnosisType` | `varchar(10)` | Y |  |  |
| `ActiveFrom` | `datetime` | Y |  | DATE |
| `ActiveTo` | `datetime` | Y |  | DATE |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimFacilities`

Dimension, 14 columns, PK: FacilityCode

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `FacilityID` | `int` | N | Y | KEY |
| `PracticeCode` | `bigint` | N |  | KEY |
| `FacilityCode` | `bigint` | N |  | KEY |
| `FacilityName` | `varchar(max)` | Y |  |  |
| `FacilityType` | `varchar(max)` | Y |  |  |
| `Address` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(50)` | Y |  |  |
| `ZIP` | `varchar(15)` | Y |  | PHI? |
| `Phone` | `varchar(15)` | Y |  | PHI? |
| `Email` | `varchar(50)` | Y |  | PHI? |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimFacilitySalesRep`

Dimension, 19 columns, PK: Practice_Facilities_Sale_Reps_Id

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `DimFacilitySalesRepID` | `bigint` | N | Y | KEY |
| `Practice_Facilities_Sale_Reps_Id` | `bigint` | N |  | KEY |
| `Practice_Facility_Id` | `bigint` | Y |  | KEY |
| `Sale_Reps_Id` | `bigint` | N |  | KEY |
| `Effective_From` | `datetime` | Y |  | DATE |
| `Effective_To` | `datetime` | Y |  | DATE |
| `First_Name` | `varchar(50)` | N |  | PHI? |
| `Last_Name` | `varchar(50)` | N |  | PHI? |
| `Middle_Initial` | `varchar(1)` | Y |  |  |
| `Date_of_Birth` | `datetime` | N |  | DATE,PHI? |
| `Email` | `varchar(100)` | Y |  | PHI? |
| `Address` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(2)` | Y |  |  |
| `Zip` | `varchar(10)` | Y |  | PHI? |
| `Country` | `varchar(100)` | Y |  |  |
| `StartDate` | `datetime` | N |  | DATE |
| `EndDate` | `datetime` | N |  | DATE |
| `Status` | `varchar(1)` | N |  |  |

### `DimFinancialClass`

Dimension, 8 columns, PK: FinancialClassID

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `FinancialClassID` | `bigint` | N |  | KEY |
| `Description` | `varchar(500)` | Y |  |  |
| `HCFA_Class` | `varchar(30)` | Y |  |  |
| `Electronic_Sub_Class` | `varchar(2)` | Y |  |  |
| `Electronic_Sub_Class_Description` | `varchar(100)` | Y |  |  |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimInsFinancialClass`

Dimension, 8 columns, PK: DimInsFinancialClass

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `DimInsFinancialClass` | `bigint` | N | Y |  |
| `Practice_Code` | `bigint` | N |  | KEY |
| `Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Ins_Address_Id` | `bigint` | N |  | KEY,PHI? |
| `Ins_Payer_Id` | `bigint` | N |  | KEY |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndtDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimInsuranceAddresses`

Dimension, 13 columns, PK: InsAddressID

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `InsAddressID` | `int` | N | Y | KEY,PHI? |
| `InsurancePayerID` | `bigint` | Y |  | KEY |
| `InsuranceAddressID` | `bigint` | Y |  | KEY,PHI? |
| `InsuranceName` | `varchar(100)` | Y |  |  |
| `Address` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(50)` | Y |  |  |
| `ZIP` | `varchar(15)` | Y |  | PHI? |
| `Phone` | `varchar(15)` | Y |  | PHI? |
| `Email` | `varchar(50)` | Y |  | PHI? |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimInsurances`

Dimension, 6 columns, PK: InsuranceID

Other indexes: `uq_DimInsurances_InsurancePayerID` (NONCLUSTERED, unique) on InsurancePayerID

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `InsuranceID` | `int` | N | Y | KEY |
| `InsurancePayerID` | `bigint` | Y |  | KEY |
| `InsuranceName` | `varchar(100)` | Y |  |  |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimLocations`

Dimension, 14 columns, PK: LocationCode

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `LocationID` | `int` | N | Y | KEY |
| `PracticeCode` | `bigint` | N |  | KEY |
| `LocationCode` | `bigint` | N |  | KEY |
| `LocationName` | `varchar(500)` | Y |  |  |
| `LocationType` | `varchar(50)` | Y |  |  |
| `Address` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(50)` | Y |  |  |
| `ZIP` | `varchar(10)` | Y |  | PHI? |
| `Phone` | `varchar(15)` | Y |  | PHI? |
| `Email` | `varchar(50)` | Y |  | PHI? |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimModifiers`

Dimension, 10 columns, PK: ModifierID

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `ModifierID` | `bigint` | N | Y | KEY |
| `Modifier` | `varchar(10)` | N |  |  |
| `Modifier_Details` | `varchar(2000)` | Y |  |  |
| `Modifier_Category` | `varchar(85)` | Y |  |  |
| `Modifier_SubCategory` | `varchar(75)` | Y |  |  |
| `Is_Pricing` | `bit` | Y |  | FLAG |
| `Is_Active` | `bit` | Y |  | FLAG |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimPatients`

Dimension, 46 columns, PK: PatientAccount

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `PatientID` | `bigint` | N | Y | KEY,PHI? |
| `PatientAccount` | `bigint` | N |  | KEY,PHI? |
| `PracticeCode` | `bigint` | Y |  | KEY |
| `Patient_First_Name` | `varchar(150)` | Y |  | PHI? |
| `Patient_Last_Name` | `varchar(150)` | Y |  | PHI? |
| `Middle_Initial` | `varchar(1)` | Y |  |  |
| `Date_of_Birth` | `datetime` | Y |  | DATE,PHI? |
| `Gender` | `varchar(20)` | Y |  |  |
| `SSN` | `varchar(20)` | Y |  | PHI? |
| `Marital_Status` | `varchar(50)` | Y |  |  |
| `Address` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(50)` | Y |  |  |
| `ZIP` | `varchar(10)` | Y |  | PHI? |
| `Home_Phone` | `varchar(15)` | Y |  | PHI? |
| `Cell_Phone` | `varchar(15)` | Y |  | PHI? |
| `Work_phone` | `varchar(15)` | Y |  | PHI? |
| `Work_Phone_EXT` | `varchar(5)` | Y |  | PHI? |
| `Email` | `varchar(50)` | Y |  | PHI? |
| `Primary_Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Primary_Insurance_ID` | `bigint` | Y |  | KEY |
| `Primary_Insurance_Name` | `varchar(500)` | Y |  |  |
| `Primary_Plan_StartDate` | `datetime` | Y |  | DATE |
| `Primary_Plan_EndDate` | `datetime` | Y |  | DATE |
| `Primary_Policy_Number` | `varchar(150)` | Y |  | PHI? |
| `Primary_Insurance_Type` | `varchar(50)` | Y |  |  |
| `Primary_Insurance_Type_Named` | `varchar(50)` | Y |  |  |
| `Secondary_Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Secondary_Insurance_ID` | `bigint` | Y |  | KEY |
| `Secondary_Insurance_Name` | `varchar(500)` | Y |  |  |
| `Secondary_Plan_StartDate` | `datetime` | Y |  | DATE |
| `Secondary_Plan_EndDate` | `datetime` | Y |  | DATE |
| `Secondary_Policy_Number` | `varchar(150)` | Y |  | PHI? |
| `Secondary_Insurance_Type` | `varchar(50)` | Y |  |  |
| `Secondary_Insurance_Type_Named` | `varchar(50)` | Y |  |  |
| `Tertiary_Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Tertiary_Insurance_ID` | `bigint` | Y |  | KEY |
| `Tertiary_Insurance_Name` | `varchar(500)` | Y |  |  |
| `Tertiary_Plan_StartDate` | `datetime` | Y |  | DATE |
| `Tertiary_Plan_EndDate` | `datetime` | Y |  | DATE |
| `Tertiary_Policy_Number` | `varchar(150)` | Y |  | PHI? |
| `Tertiary_Insurance_Type` | `varchar(50)` | Y |  |  |
| `Tertiary_Insurance_Type_Named` | `varchar(50)` | Y |  |  |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimPracticeBillingProviders`

Dimension, 4 columns, PK: ID

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `ID` | `int` | N | Y | KEY |
| `Provider_Code` | `bigint` | Y |  | KEY |
| `Practice_Code` | `bigint` | Y |  | KEY |
| `Billing_Provider` | `varchar(500)` | Y |  |  |

### `DimPracticePayers`

Dimension, 19 columns, PK: PracticePayerID

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `PracticePayerID` | `int` | N | Y | KEY |
| `Practice_Code` | `bigint` | N |  | KEY |
| `InsurancePayerID` | `bigint` | Y |  | KEY |
| `InsuranceAddressID` | `bigint` | Y |  | KEY,PHI? |
| `InsuranceName` | `varchar(100)` | Y |  |  |
| `Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Financial_Class_Description` | `varchar(255)` | Y |  |  |
| `Address1` | `varchar(500)` | Y |  | PHI? |
| `Address2` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(50)` | Y |  |  |
| `ZIP` | `varchar(15)` | Y |  | PHI? |
| `Phone` | `varchar(15)` | Y |  | PHI? |
| `ActiveInsurancePayer` | `bit` | Y |  | FLAG |
| `Submission_Type` | `varchar(10)` | Y |  |  |
| `ActiveInsuranceAddress` | `bit` | Y |  | FLAG,PHI? |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimPracticePayers_Filter`

Dimension, 4 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `ID` | `int` | N | Y | KEY |
| `Practice_Code` | `bigint` | Y |  | KEY |
| `Ins_Payer_ID` | `bigint` | Y |  | KEY |
| `Ins_Payer_Name` | `varchar(500)` | Y |  |  |

### `DimPractices`

Dimension, 15 columns, PK: PracticeCode

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `PracticeID` | `int` | N | Y | KEY |
| `PracticeCode` | `bigint` | N |  | KEY |
| `PracticeName` | `varchar(500)` | Y |  |  |
| `Alias` | `varchar(50)` | Y |  |  |
| `Address` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(50)` | Y |  |  |
| `ZIP` | `varchar(10)` | Y |  | PHI? |
| `Phone` | `varchar(15)` | Y |  | PHI? |
| `Email` | `varchar(50)` | Y |  | PHI? |
| `Is_Active` | `bit` | Y |  | FLAG |
| `Specialty_Id` | `varchar(10)` | Y |  | KEY |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimProviders`

Dimension, 14 columns, PK: ProviderCode

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `ProviderID` | `int` | N | Y | KEY |
| `PracticeCode` | `bigint` | N |  | KEY |
| `ProviderCode` | `bigint` | N |  | KEY |
| `ProviderName` | `varchar(500)` | Y |  |  |
| `Specialty` | `varchar(100)` | Y |  |  |
| `Address` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(50)` | Y |  |  |
| `ZIP` | `varchar(10)` | Y |  | PHI? |
| `Phone` | `varchar(15)` | Y |  | PHI? |
| `Email` | `varchar(150)` | Y |  | PHI? |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |
| `Status` | `varchar(15)` | Y |  |  |

### `DimReportClaimInsurance`

Dimension, 21 columns, PK: Claim_Insurance_Id

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Claim_Insurance_Id` | `bigint` | N |  | KEY |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Practice_Code` | `bigint` | Y |  | KEY |
| `Patient_Account` | `bigint` | Y |  | KEY,PHI? |
| `Claim_Type` | `varchar(50)` | Y |  |  |
| `Billing_Status` | `varchar(30)` | Y |  |  |
| `Policy_Number` | `varchar(80)` | Y |  | PHI? |
| `Insurance_Type` | `varchar(15)` | Y |  |  |
| `Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Ins_Address_Id` | `bigint` | Y |  | KEY,PHI? |
| `Subscriber_Relationship` | `varchar(20)` | Y |  | PHI? |
| `Description` | `varchar(55)` | Y |  |  |
| `Ins_Payer_Name` | `varchar(100)` | Y |  |  |
| `Ins_Assigned_Payer_Id` | `varchar(50)` | Y |  | KEY |
| `Payment_Due_Days` | `int` | Y |  |  |
| `Claim_Modified_Date` | `datetime` | Y |  | DATE |
| `Modified_Date` | `datetime` | Y |  | DATE |
| `Claim_Deleted` | `bit` | Y |  | FLAG |
| `Deleted` | `bit` | Y |  | FLAG |
| `Row_Version` | `varbinary(8000)` | Y |  |  |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |

### `DimReportClaimPhysicians`

Dimension, 9 columns, PK: Physician_Id

Other indexes: `UK_ReportClaimPhysicians` (NONCLUSTERED, unique) on NPI

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Physician_Id` | `bigint` | N |  | KEY |
| `NPI` | `bigint` | N |  |  |
| `Phy_Name` | `varchar(75)` | Y |  |  |
| `Last_Name` | `varchar(60)` | Y |  | PHI? |
| `First_Name` | `varchar(50)` | Y |  | PHI? |
| `Middle_Initial` | `varchar(50)` | Y |  |  |
| `Deleted` | `bit` | Y |  | FLAG |
| `Last_Sync_Date` | `datetime` | Y |  | DATE |
| `row_version` | `varbinary(8000)` | Y |  |  |

### `DimSalesRep`

Dimension, 16 columns, PK: Practice_sales_Rep_Id

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `DimSalesRepID` | `bigint` | N | Y | KEY |
| `Practice_sales_Rep_Id` | `bigint` | N |  | KEY |
| `Practice_code` | `bigint` | Y |  | KEY |
| `First_Name` | `varchar(50)` | N |  | PHI? |
| `Last_Name` | `varchar(50)` | N |  | PHI? |
| `Middle_Initial` | `varchar(1)` | Y |  |  |
| `Date_of_Birth` | `datetime` | N |  | DATE,PHI? |
| `Email` | `varchar(100)` | Y |  | PHI? |
| `Address` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(2)` | Y |  |  |
| `Zip` | `varchar(10)` | Y |  | PHI? |
| `Country` | `varchar(100)` | Y |  |  |
| `StartDate` | `datetime` | N |  | DATE |
| `EndDate` | `datetime` | N |  | DATE |
| `Status` | `varchar(1)` | N |  |  |

### `FACT_CC_Claims_Payment_Count`

Fact/report, 33 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Claim_No` | `bigint` | Y |  | KEY |
| `LocationCode` | `bigint` | Y |  | KEY |
| `Location_Name` | `varchar(100)` | Y |  |  |
| `POS_Name` | `varchar(83)` | Y |  |  |
| `Billing_Provider` | `varchar(183)` | Y |  |  |
| `Payer_Id` | `bigint` | Y |  | KEY |
| `Payer_Name` | `varchar(100)` | Y |  |  |
| `Financial_Class` | `varchar(55)` | Y |  |  |
| `Procedure_Code` | `varchar(5)` | Y |  | KEY |
| `Sequence_Number` | `bigint` | Y |  |  |
| `Date_of_Service` | `date` | Y |  | DATE |
| `Posting_Date` | `date` | Y |  | DATE |
| `Deposit_Date` | `date` | Y |  | DATE |
| `Check_Date` | `date` | Y |  | DATE |
| `Responsible_Party` | `varchar(30)` | Y |  |  |
| `Amount_Paid` | `money` | Y |  | MEASURE |
| `Amount_Adjusted` | `money` | Y |  | MEASURE |
| `Withheld_Amount` | `money` | Y |  | MEASURE |
| `InsurancePayments` | `money` | Y |  | MEASURE |
| `PatientPayments` | `money` | Y |  | MEASURE |
| `Total_Payment` | `money` | Y |  | MEASURE |
| `Rendering_Provider_ID` | `bigint` | Y |  | KEY |
| `Rendering_Provider` | `varchar(183)` | Y |  |  |
| `Charge_Amount` | `money` | Y |  | MEASURE |
| `DOE` | `date` | Y |  | DATE |
| `Billing_Status` | `varchar(30)` | Y |  |  |
| `Claim_Category_Name` | `varchar(300)` | Y |  |  |
| `InsuranceAmountPaid` | `money` | Y |  | MEASURE |
| `PatientAmountPaid` | `money` | Y |  | MEASURE |
| `Id` | `bigint` | N | Y | KEY |
| `Outstanding_Balance` | `money` | Y |  | MEASURE |
| `POS` | `varchar(55)` | Y |  |  |
| `Practice_Code` | `bigint` | Y |  | KEY |

### `Fact_DaysAR`

Fact/report, 8 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Claim_No` | `bigint` | Y |  | KEY |
| `Due_Amount` | `money` | Y |  | MEASURE |
| `Charge_Amount` | `money` | Y |  | MEASURE |
| `Billed_Date` | `datetime` | Y |  | DATE |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Location_ID` | `bigint` | Y |  | KEY |
| `Provider_ID` | `bigint` | Y |  | KEY |
| `Practice_Code` | `bigint` | Y |  | KEY |

### `Fact_Interg`

Fact/report, 26 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Fact_Interg_Id` | `bigint` | N | Y | KEY |
| `Type` | `varchar(100)` | Y |  |  |
| `Patient Name` | `varchar(200)` | Y |  |  |
| `Acct Nbr` | `bigint` | Y |  |  |
| `Payer Name` | `varchar(200)` | Y |  |  |
| `Billed Claim ID` | `bigint` | Y |  | KEY |
| `DOS` | `date` | Y |  | DATE |
| `Status` | `varchar(50)` | Y |  |  |
| `Charge Responsibility` | `varchar(100)` | Y |  | MEASURE |
| `Billing Provider` | `varchar(200)` | Y |  |  |
| `Appt Svc Cntr Name` | `varchar(200)` | Y |  |  |
| `Denial` | `varchar(100)` | Y |  |  |
| `Reason` | `varchar(200)` | Y |  |  |
| `Number of Days between DOS and Date Billed` | `int` | Y |  | DATE |
| `Provider Name` | `varchar(200)` | Y |  |  |
| `Length of Stay` | `int` | Y |  |  |
| `Appt Svc Cntr POS Code` | `varchar(150)` | Y |  | KEY |
| `First Billed Date` | `date` | Y |  | DATE |
| `Last Billed Date` | `date` | Y |  | DATE |
| `Charge Amount` | `money` | Y |  | MEASURE |
| `Payment Received` | `money` | Y |  | MEASURE |
| `Adjusted Amount` | `money` | Y |  | MEASURE |
| `Balance` | `money` | Y |  | MEASURE |
| `Expected Recoverable` | `money` | Y |  | MEASURE |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |

### `Fact_Interg_Data`

Fact/report, 26 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Fact_Interg_Id` | `bigint` | N | Y | KEY |
| `Type` | `varchar(100)` | Y |  |  |
| `Patient_Name` | `varchar(200)` | Y |  | PHI? |
| `Patient_Account` | `bigint` | Y |  | KEY,PHI? |
| `Payer_Name` | `varchar(200)` | Y |  |  |
| `Billed_Claim_Id` | `bigint` | Y |  | KEY |
| `DOS` | `date` | Y |  | DATE |
| `Status` | `varchar(50)` | Y |  |  |
| `Charge_Responsibility` | `varchar(100)` | Y |  | MEASURE |
| `Billing_Provider` | `varchar(200)` | Y |  |  |
| `Appt_Svc_Cntr_Name` | `varchar(200)` | Y |  |  |
| `Denial` | `varchar(100)` | Y |  |  |
| `Reason` | `varchar(200)` | Y |  |  |
| `Number_of_Days_between_DOS_and_DateBilled` | `int` | Y |  | DATE |
| `Provider_Name` | `varchar(200)` | Y |  |  |
| `Length_of_Stay` | `int` | Y |  |  |
| `Appt_Svc_Cntr_POS_Code` | `varchar(150)` | Y |  | KEY |
| `First_Billed_Date` | `date` | Y |  | DATE |
| `Last_Billed_Date` | `date` | Y |  | DATE |
| `Charge_Amount` | `money` | Y |  | MEASURE |
| `Payment_Received` | `money` | Y |  | MEASURE |
| `Adjusted_Amount` | `money` | Y |  | MEASURE |
| `Balance` | `money` | Y |  | MEASURE |
| `Expected_Recoverable` | `money` | Y |  | MEASURE |
| `StartDate` | `datetime` | Y |  | DATE |
| `EndDate` | `datetime` | Y |  | DATE |

### `FactAccountReceivable`

Fact/report, 13 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Balance` | `varchar(15)` | Y |  | MEASURE |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Charge_Reponsibility` | `varchar(500)` | Y |  | MEASURE |
| `Client_Bill` | `bit` | Y |  | FLAG |
| `Is_Draft` | `bit` | Y |  | FLAG |
| `Billing_Status` | `varchar(500)` | Y |  |  |
| `Date_of_Service` | `datetime` | Y |  | DATE |
| `Procedure_Code` | `varchar(5)` | Y |  | KEY |
| `Sequence_Number` | `bigint` | Y |  |  |
| `Sales_Rep` | `bigint` | Y |  |  |
| `Claim_Type` | `varchar(150)` | Y |  |  |
| `Due_Amount` | `float` | Y |  | MEASURE |
| `Practice_Code` | `bigint` | Y |  | KEY |

### `FactClaimCount`

Fact/report, 32 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Patient_Account` | `bigint` | Y |  | KEY,PHI? |
| `Patient_Name` | `varchar(300)` | Y |  | PHI? |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Procedure_Code` | `varchar(20)` | Y |  | KEY |
| `Sequence_Number` | `bigint` | Y |  |  |
| `Practice_Location_Id` | `bigint` | Y |  | KEY |
| `Location_Name` | `varchar(100)` | Y |  |  |
| `Location_Type` | `varchar(100)` | Y |  |  |
| `Address_Line1` | `varchar(100)` | Y |  | PHI? |
| `Zip` | `varchar(100)` | Y |  | PHI? |
| `City` | `varchar(100)` | Y |  |  |
| `State` | `varchar(100)` | Y |  |  |
| `Country` | `varchar(100)` | Y |  |  |
| `Ins_Payer_Name` | `varchar(100)` | Y |  |  |
| `Financial_Class` | `varchar(100)` | Y |  |  |
| `POS` | `varchar(30)` | Y |  |  |
| `Claim_Category_Name` | `varchar(300)` | Y |  |  |
| `Billing_Status` | `varchar(30)` | Y |  |  |
| `Billing_Provider` | `varchar(130)` | Y |  |  |
| `Rendering_Provider` | `varchar(130)` | Y |  |  |
| `DOS` | `date` | Y |  | DATE |
| `DOE` | `date` | Y |  | DATE |
| `POS_Name` | `varchar(200)` | Y |  |  |
| `Charge_Amount` | `money` | Y |  | MEASURE |
| `Amount_Paid` | `money` | Y |  | MEASURE |
| `Amount_Adjusted` | `money` | Y |  | MEASURE |
| `Withheld_Amount` | `money` | Y |  | MEASURE |
| `Insurance_Paid` | `money` | Y |  | MEASURE |
| `Patient_Paid` | `money` | Y |  | MEASURE |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Rendering_Provider_ID` | `bigint` | Y |  | KEY |
| `Practice_Code` | `bigint` | Y |  | KEY |

### `FactClaimCptReport`

Fact/report, 48 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Practice_Code` | `bigint` | Y |  | KEY |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Created_Date` | `date` | Y |  | DATE |
| `Practice_Facility_Id` | `bigint` | Y |  | KEY |
| `Location_ID` | `bigint` | Y |  | KEY |
| `Billing_Provider` | `bigint` | Y |  |  |
| `Billing_Status` | `varchar(30)` | Y |  |  |
| `Is_Draft` | `bit` | Y |  | FLAG |
| `Client_Bill` | `bit` | Y |  | FLAG |
| `Sales_Rep_id` | `bigint` | Y |  | KEY |
| `Sales_Rep` | `varchar(250)` | Y |  |  |
| `Claim_charges_Id` | `bigint` | Y |  | KEY |
| `DOS` | `date` | Y |  | DATE |
| `DOE` | `date` | Y |  | DATE |
| `Procedure_Code` | `varchar(50)` | Y |  | KEY |
| `Sequence_Number` | `bigint` | Y |  |  |
| `P_Sequence_Number` | `bigint` | Y |  |  |
| `Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Financial_Class` | `varchar(100)` | Y |  |  |
| `Charge_Amount` | `money` | Y |  | MEASURE |
| `Expected_Fee` | `money` | Y |  | MEASURE |
| `Units` | `decimal(18,2)` | Y |  | MEASURE |
| `DOP` | `date` | Y |  | DATE |
| `Check_Date` | `date` | Y |  | DATE |
| `Insurance_Addresses_Id` | `bigint` | Y |  | KEY,PHI? |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Insurance_Type` | `varchar(15)` | Y |  |  |
| `Responsible_Party` | `varchar(30)` | Y |  |  |
| `Amount_Approved` | `decimal(38,2)` | Y |  | MEASURE |
| `Insurance_Payments` | `money` | Y |  | MEASURE |
| `Patient_Payments` | `money` | Y |  | MEASURE |
| `Client_Bill_Payments` | `money` | Y |  | MEASURE |
| `Contractual_Adjustments` | `money` | Y |  | MEASURE |
| `Manual_Adjustments` | `money` | Y |  | MEASURE |
| `Withheld_Amount` | `money` | Y |  | MEASURE |
| `Copay` | `money` | Y |  | MEASURE |
| `Deductible` | `money` | Y |  | MEASURE |
| `Coinsurance` | `money` | Y |  | MEASURE |
| `Insurance_Balance` | `money` | Y |  | MEASURE |
| `Patient_Balance` | `money` | Y |  | MEASURE |
| `Paid` | `bigint` | Y |  | KEY |
| `ADJ` | `bigint` | Y |  |  |
| `Billed_Date` | `date` | Y |  | DATE |
| `Last_Billed_Date` | `date` | Y |  | DATE |
| `Paid_To_Patient` | `money` | Y |  | MEASURE |
| `COGS` | `money` | Y |  | MEASURE |
| `Include_in_submission` | `bit` | Y |  | FLAG |
| `PCharge_Amount` | `money` | Y |  | MEASURE |

### `FactClaimInterestReport`

Fact/report, 22 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Practice_Code` | `bigint` | Y |  | KEY |
| `Claim_NO` | `bigint` | Y |  | KEY |
| `Practice_Facility_Id` | `bigint` | Y |  | KEY |
| `Created_Date` | `date` | Y |  | DATE |
| `Location_ID` | `bigint` | Y |  | KEY |
| `Billing_Provider` | `bigint` | Y |  |  |
| `Billing_Status` | `varchar(30)` | Y |  |  |
| `Is_Draft` | `bit` | Y |  | FLAG |
| `Client_Bill` | `bit` | Y |  | FLAG |
| `Sales_Rep_id` | `bigint` | Y |  | KEY |
| `Sales_Rep` | `varchar(104)` | Y |  |  |
| `DOS` | `date` | Y |  | DATE |
| `DOE` | `date` | Y |  | DATE |
| `DOP` | `date` | Y |  | DATE |
| `Check_Date` | `date` | Y |  | DATE |
| `Insurance_Addresses_Id` | `bigint` | Y |  | KEY,PHI? |
| `Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Interest_Amount` | `decimal(38,2)` | Y |  | MEASURE |
| `Financial_Class` | `varchar(55)` | Y |  |  |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Billed_Date` | `date` | Y |  | DATE |
| `Last_Billed_Date` | `date` | Y |  | DATE |

### `FactClaimResubmission`

Fact/report, 14 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `ID` | `int` | N | Y | KEY |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Billing_Provider_Id` | `bigint` | Y |  | KEY |
| `Billing_Provider` | `varchar(130)` | Y |  |  |
| `Submission_Type` | `varchar(50)` | Y |  |  |
| `DOS` | `datetime` | Y |  | DATE |
| `Submission_Date` | `datetime` | Y |  | DATE |
| `Practice_Location_Id` | `bigint` | Y |  | KEY |
| `Location_Name` | `varchar(100)` | Y |  |  |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Ins_Payer_Name` | `varchar(100)` | Y |  |  |
| `Insurance_Type` | `varchar(5)` | Y |  |  |
| `RowNumDateOfSubmission` | `int` | Y |  | DATE |
| `Practice_Code` | `bigint` | Y |  | KEY |

### `FactClaimsSubmissionAccountReceivable`

Fact/report, 5 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Claim_No` | `bigint` | Y |  | KEY |
| `Date_of_Service` | `datetime` | Y |  | DATE |
| `Procedure_Code` | `varchar(5)` | Y |  | KEY |
| `Created_Date` | `datetime` | Y |  | DATE |
| `Deleted` | `bit` | Y |  | FLAG |

### `FactCPT_KPI`

Fact/report, 99 columns, PK: FactCPT_KPI_ID

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `FactCPT_KPI_ID` | `int` | N | Y | KEY |
| `Accession_No` | `varchar(20)` | Y |  | KEY,PHI? |
| `Withheld_Amount` | `money` | N |  | MEASURE |
| `Patient_Account` | `bigint` | N |  | KEY,PHI? |
| `Sales_Rep` | `varchar(54)` | N |  |  |
| `Rejected_By` | `varchar(25)` | Y |  |  |
| `Is_Draft` | `bit` | Y |  | FLAG |
| `Hold_Submission` | `bit` | N |  | FLAG |
| `Hold_Submission_Reason` | `varchar(200)` | N |  |  |
| `Claim_Category_Name` | `varchar(300)` | N |  |  |
| `Assigned_To` | `varchar(8000)` | Y |  |  |
| `Patient` | `varchar(54)` | N |  | PHI? |
| `P_Alias` | `varchar(100)` | N |  |  |
| `L_Alias` | `varchar(20)` | N |  |  |
| `DOB` | `datetime` | N |  | DATE,PHI? |
| `Gender` | `varchar(20)` | N |  |  |
| `Patient_Primary_Phone` | `varchar(15)` | Y |  | PHI? |
| `Patient_Secondary_Phone` | `varchar(15)` | Y |  | PHI? |
| `Claim_No` | `bigint` | N |  | KEY |
| `DOS` | `datetime` | Y |  | DATE |
| `Start_DOS` | `datetime` | Y |  | DATE |
| `End_DOS` | `datetime` | Y |  | DATE |
| `CPT` | `varchar(50)` | Y |  |  |
| `Modifier` | `varchar(14)` | Y |  |  |
| `ICD1` | `varchar(8)` | N |  |  |
| `Diagnosis` | `varchar(8000)` | Y |  |  |
| `Billing_Provider` | `varchar(233)` | N |  |  |
| `Rendering_Provider` | `varchar(233)` | N |  |  |
| `Location_Name` | `varchar(100)` | Y |  |  |
| `Practice_Location_Id` | `bigint` | Y |  | KEY |
| `Patient_Primary_Location` | `varchar(100)` | Y |  |  |
| `Units` | `numeric(9,2)` | Y |  | MEASURE |
| `Charge_Amount` | `money` | N |  | MEASURE |
| `Expected_Fee` | `numeric(38,2)` | Y |  | MEASURE |
| `Patient_Responsibility` | `money` | Y |  | MEASURE |
| `Sequence_Number` | `int` | N |  |  |
| `Entered_Date` | `datetime` | N |  | DATE |
| `Entered_By` | `varchar(133)` | Y |  |  |
| `Practice_Name` | `varchar(100)` | N |  |  |
| `Tax_Id` | `varchar(10)` | N |  | KEY |
| `Authorization_Number` | `varchar(50)` | Y |  |  |
| `Posting_Date` | `date` | Y |  | DATE |
| `Responsible_Party_Name` | `varchar(100)` | N |  |  |
| `Referring_Physician` | `varchar(153)` | N |  |  |
| `Supervising_Physician` | `varchar(232)` | N |  |  |
| `Primary_Policy_Type` | `varchar(55)` | N |  | PHI? |
| `Place_of_Service` | `varchar(75)` | N |  |  |
| `Insurance_Payments` | `money` | N |  | MEASURE |
| `Patient_Payments` | `money` | N |  | MEASURE |
| `Contractual_Adjustments` | `money` | N |  | MEASURE |
| `Manual_Adjustments` | `money` | N |  | MEASURE |
| `Total_Paid` | `money` | N |  | MEASURE |
| `Due_Amount` | `money` | Y |  | MEASURE |
| `Payment_Due_Days` | `int` | Y |  |  |
| `Referring_Facility` | `varchar(100)` | Y |  |  |
| `Client_Bill_Balance` | `money` | N |  | MEASURE |
| `Case_Number` | `bigint` | N |  |  |
| `Primary_Insurance_ID` | `varchar(25)` | N |  | KEY |
| `Primary_Subscriber_Number` | `varchar(80)` | N |  | PHI? |
| `Secondary_Insurance` | `varchar(100)` | N |  |  |
| `Secondary_Insurance_ID` | `varchar(25)` | N |  | KEY |
| `Secondary_Policy_Type` | `varchar(55)` | N |  | PHI? |
| `Secondary_Subscriber_Number` | `varchar(80)` | N |  | PHI? |
| `Tertiary_Insurance` | `varchar(100)` | N |  |  |
| `Tertiary_Insurance_ID` | `varchar(25)` | N |  | KEY |
| `Tertiary_Policy_Type` | `varchar(55)` | N |  | PHI? |
| `Tertiary_Subscriber_Number` | `varchar(80)` | N |  | PHI? |
| `Billing_Status` | `varchar(50)` | Y |  |  |
| `Referral_Source` | `varchar(100)` | Y |  |  |
| `Entered_date_by_month` | `varchar(7)` | Y |  | DATE |
| `Posting_date_by_month` | `varchar(7)` | Y |  | DATE |
| `DOS_date_by_month` | `varchar(7)` | Y |  | DATE |
| `Status` | `nvarchar(100)` | Y |  |  |
| `Charge_Reponsibility` | `nvarchar(100)` | Y |  | MEASURE |
| `Insurance_Balance` | `money` | Y |  | MEASURE |
| `Patient_Balance` | `money` | Y |  | MEASURE |
| `Billed_Date` | `date` | Y |  | DATE |
| `Last_Billed_Date` | `date` | Y |  | DATE |
| `Billed_date_by_month` | `varchar(7)` | Y |  | DATE |
| `Last_billed_date_by_month` | `varchar(7)` | Y |  | DATE |
| `TotalDue` | `money` | Y |  | MEASURE |
| `Phy_Name` | `varchar(75)` | Y |  |  |
| `Provider_Code` | `bigint` | Y |  | KEY |
| `rpProviderCode` | `bigint` | Y |  | KEY |
| `Supervising_Provider` | `bigint` | Y |  |  |
| `Ordering_Provider` | `bigint` | Y |  |  |
| `Ordering_Provider_Name` | `varchar(232)` | N |  |  |
| `Panel_Name` | `varchar(300)` | Y |  |  |
| `Performing_Lab` | `varchar(100)` | N |  |  |
| `CPT_Description` | `varchar(500)` | Y |  |  |
| `Claim_Type` | `varchar(50)` | Y |  |  |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Practice_Code` | `bigint` | N |  | KEY |
| `Revenue_Code` | `varchar(50)` | Y |  | KEY |
| `Practice_Facility_Id` | `bigint` | Y |  | KEY |
| `Billing_Provider_Code` | `bigint` | Y |  | KEY |
| `Practice_sales_Rep_Id` | `bigint` | Y |  | KEY |
| `Expected_Reimbursement` | `money` | Y |  | MEASURE |

### `FactDaysARbyPayer`

Fact/report, 14 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Practice_Code` | `bigint` | Y |  | KEY |
| `Ins_Payer_Name` | `varchar(500)` | Y |  |  |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Location_ID` | `bigint` | Y |  | KEY |
| `Provider_ID` | `bigint` | Y |  | KEY |
| `Insurance_Type` | `varchar(15)` | Y |  |  |
| `Insurance_Addresses_Id` | `bigint` | Y |  | KEY,PHI? |
| `Procedure_Code` | `varchar(50)` | Y |  | KEY |
| `Sequence_Number` | `bigint` | Y |  |  |
| `Charge_Amount` | `money` | Y |  | MEASURE |
| `Billed_Date` | `date` | Y |  | DATE |
| `Insurance_Balance` | `money` | Y |  | MEASURE |
| `Patient_Balance` | `money` | Y |  | MEASURE |

### `FactDaysToBill`

Fact/report, 14 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Claim_No` | `bigint` | Y |  | KEY |
| `First_Submission_Date` | `date` | Y |  | DATE |
| `Practice_Location_Id` | `bigint` | Y |  | KEY |
| `Billing_Provider_Id` | `bigint` | Y |  | KEY |
| `Billing_Provider` | `varchar(130)` | Y |  |  |
| `DOS` | `date` | Y |  | DATE |
| `DOE` | `date` | Y |  | DATE |
| `Location_Name` | `varchar(100)` | Y |  |  |
| `Charge_Amount` | `money` | Y |  | MEASURE |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Ins_Payer_Name` | `varchar(100)` | Y |  |  |
| `DOSAge` | `int` | Y |  |  |
| `DOEAge` | `int` | Y |  |  |
| `Practice_Code` | `bigint` | Y |  | KEY |

### `FactFacilitySalesRep`

Fact/report, 19 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `FactFacilitySalesRepID` | `bigint` | N | Y | KEY |
| `Practice_Facilities_Sale_Reps_Id` | `bigint` | Y |  | KEY |
| `Practice_Facility_Id` | `bigint` | Y |  | KEY |
| `Sale_Reps_Id` | `bigint` | Y |  | KEY |
| `Effective_From` | `datetime` | Y |  | DATE |
| `Effective_To` | `datetime` | Y |  | DATE |
| `First_Name` | `varchar(50)` | Y |  | PHI? |
| `Last_Name` | `varchar(50)` | Y |  | PHI? |
| `Middle_Initial` | `varchar(1)` | Y |  |  |
| `Date_of_Birth` | `datetime` | Y |  | DATE,PHI? |
| `Email` | `varchar(100)` | Y |  | PHI? |
| `Address` | `varchar(500)` | Y |  | PHI? |
| `City` | `varchar(50)` | Y |  |  |
| `State` | `varchar(2)` | Y |  |  |
| `Zip` | `varchar(10)` | Y |  | PHI? |
| `Country` | `varchar(100)` | Y |  |  |
| `StartDate` | `datetime` | N |  | DATE |
| `EndDate` | `datetime` | N |  | DATE |
| `Status` | `varchar(1)` | N |  |  |

### `FactFinancialClass`

Fact/report, 16 columns, PK: FactFinancialClassID

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `FactFinancialClassID` | `int` | N | Y | KEY |
| `TotalClaims` | `bigint` | N |  |  |
| `Totaldenials` | `bigint` | Y |  |  |
| `TotalPayClaims` | `bigint` | Y |  |  |
| `Financial_Class_ID` | `bigint` | Y |  | KEY |
| `Financial_Class` | `varchar(55)` | Y |  |  |
| `Practice_Facility_Id` | `bigint` | Y |  | KEY |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Date_of_Entry` | `date` | Y |  | DATE |
| `Date_of_Service` | `date` | Y |  | DATE |
| `Total_Paid` | `money` | Y |  | MEASURE |
| `Location_ID` | `bigint` | Y |  | KEY |
| `Billing_Provider` | `bigint` | Y |  |  |
| `Sales_Rep` | `bigint` | Y |  |  |
| `Practice_Code` | `bigint` | Y |  | KEY |
| `Check_Date` | `datetime` | Y |  | DATE |

### `FactGlobalKPIClaimData`

Fact/report, 24 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Practice_code` | `bigint` | Y |  | KEY |
| `Practice_Name` | `varchar(100)` | Y |  |  |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Accession_No` | `varchar(50)` | Y |  | KEY,PHI? |
| `Patient_Name` | `varchar(114)` | Y |  | PHI? |
| `Date_of_Birth` | `datetime` | Y |  | DATE,PHI? |
| `Date_of_Service` | `datetime` | Y |  | DATE |
| `Claim_Category_Name` | `varchar(300)` | Y |  |  |
| `Primary_Insurance` | `varchar(100)` | Y |  |  |
| `Primary_Subscriber_Number` | `varchar(80)` | Y |  | PHI? |
| `Claim_Type` | `varchar(13)` | Y |  |  |
| `Claim_Status` | `varchar(50)` | Y |  |  |
| `Entered_Date` | `datetime` | Y |  | DATE |
| `Entered_By` | `varchar(55)` | Y |  |  |
| `First_Billed_Date` | `datetime` | Y |  | DATE |
| `Last_Billed_Date` | `datetime` | Y |  | DATE |
| `Charge_Amount` | `money` | Y |  | MEASURE |
| `Allowed` | `decimal(38,2)` | Y |  | MEASURE |
| `Insurance_Payments` | `money` | Y |  | MEASURE |
| `Patient_Payments` | `money` | Y |  | MEASURE |
| `Contractual_Adjustments` | `money` | Y |  | MEASURE |
| `Manual_Adjustments` | `money` | Y |  | MEASURE |
| `Withheld_Amount` | `money` | Y |  | MEASURE |
| `Outstanding_Amount` | `money` | Y |  | MEASURE |

### `FactGlobalKPIClaimSubmissionsHistory`

Fact/report, 9 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Practice_Code` | `bigint` | Y |  | KEY |
| `Claim_Type` | `varchar(13)` | Y |  |  |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Submission_Type` | `varchar(20)` | Y |  |  |
| `Insurance_Type` | `varchar(20)` | Y |  |  |
| `Ins_Payer_ID` | `bigint` | Y |  | KEY |
| `Submission_Date_Time` | `date` | Y |  | DATE |
| `Claim_Status` | `varchar(10)` | Y |  |  |
| `Claim_Status_Date` | `date` | Y |  | DATE |

### `FactGlobalKPICPTClaimData`

Fact/report, 29 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Practice_code` | `bigint` | Y |  | KEY |
| `Practice_Name` | `varchar(100)` | Y |  |  |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Accession_No` | `varchar(50)` | Y |  | KEY,PHI? |
| `Patient_Name` | `varchar(114)` | Y |  | PHI? |
| `Date_of_Birth` | `datetime` | Y |  | DATE,PHI? |
| `Date_of_Service` | `datetime` | Y |  | DATE |
| `Claim_Category_Name` | `varchar(300)` | Y |  |  |
| `Primary_Insurance` | `varchar(100)` | Y |  |  |
| `Primary_Subscriber_Number` | `varchar(80)` | Y |  | PHI? |
| `CPT_Codes` | `varchar(50)` | Y |  |  |
| `CPT_Id` | `bigint` | Y |  | KEY |
| `Diagnosis` | `varchar(8)` | Y |  |  |
| `Claim_Type` | `varchar(13)` | Y |  |  |
| `Status` | `nvarchar(100)` | Y |  |  |
| `Charge_Reponsibility` | `nvarchar(100)` | Y |  | MEASURE |
| `Claim_Status` | `varchar(50)` | Y |  |  |
| `Entered_Date` | `datetime` | Y |  | DATE |
| `Entered_By` | `varchar(55)` | Y |  |  |
| `First_Billed_Date` | `datetime` | Y |  | DATE |
| `Last_Billed_Date` | `datetime` | Y |  | DATE |
| `Charge_Amount` | `money` | Y |  | MEASURE |
| `Allowed` | `decimal(38,2)` | Y |  | MEASURE |
| `Insurance_Payments` | `money` | Y |  | MEASURE |
| `Patient_Payments` | `money` | Y |  | MEASURE |
| `Contractual_Adjustments` | `money` | Y |  | MEASURE |
| `Manual_Adjustments` | `money` | Y |  | MEASURE |
| `Withheld_Amount` | `money` | Y |  | MEASURE |
| `Outstanding_Amount` | `money` | Y |  | MEASURE |

### `FactPayerRejectionReason`

Fact/report, 19 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `ID` | `int` | N | Y | KEY |
| `Claim_No` | `bigint` | Y |  | KEY |
| `Status_Reason` | `varchar(250)` | Y |  |  |
| `Billing_Provider_Id` | `bigint` | Y |  | KEY |
| `Billing_Provider` | `varchar(130)` | Y |  |  |
| `DOS` | `date` | Y |  | DATE |
| `DOE` | `date` | Y |  | DATE |
| `Location_Name` | `varchar(100)` | Y |  |  |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Ins_Payer_Name` | `varchar(100)` | Y |  |  |
| `Submission_Date` | `date` | Y |  | DATE |
| `Claim_Status` | `varchar(10)` | Y |  |  |
| `Practice_Location_Id` | `bigint` | Y |  | KEY |
| `Claim_Status_Date` | `date` | Y |  | DATE |
| `Claim_Status_Reason` | `varchar(max)` | Y |  |  |
| `Procedure_Code` | `varchar(max)` | Y |  | KEY |
| `Diagnosis` | `varchar(max)` | Y |  |  |
| `Rendering_Provider_Name` | `varchar(150)` | Y |  |  |
| `Practice_Code` | `bigint` | Y |  | KEY |

### `FactReportClaimPaymentDetails`

Fact/report, 82 columns, PK: Claim_Payment_Id

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `Claim_Payment_Id` | `bigint` | N |  | KEY |
| `Batch_Number` | `bigint` | Y |  |  |
| `Claim_No` | `bigint` | N |  | KEY |
| `Practice_Code` | `bigint` | N |  | KEY |
| `Practice` | `varchar(123)` | Y |  |  |
| `Claim_Category_Id` | `bigint` | Y |  | KEY |
| `Assigned_To_User` | `bigint` | Y |  |  |
| `Claim_Category_Name` | `varchar(300)` | Y |  |  |
| `AssignedTo` | `varchar(133)` | Y |  |  |
| `PatientPayment_Faciltiy` | `varchar(300)` | Y |  | MEASURE |
| `Practice_Location_Id` | `bigint` | Y |  | KEY |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `PatPractice_Facility_Id` | `bigint` | Y |  | KEY |
| `PmntPractice_Facility_Id` | `bigint` | Y |  | KEY |
| `Financial_Class_ID` | `bigint` | Y |  | KEY |
| `pfacPractice_Facility_Id` | `bigint` | Y |  | KEY |
| `Claim_Modified_Date` | `datetime` | Y |  | DATE |
| `Claim_Deleted` | `bit` | Y |  | FLAG |
| `Active_Practice` | `bit` | Y |  | FLAG |
| `P_Alias` | `varchar(100)` | Y |  |  |
| `L_Alias` | `varchar(50)` | Y |  |  |
| `Referring_Source` | `varchar(300)` | Y |  |  |
| `Billing_Status` | `varchar(30)` | Y |  |  |
| `Claim_Type` | `varchar(50)` | Y |  |  |
| `Accession_No` | `varchar(50)` | Y |  | KEY,PHI? |
| `Patient_Account` | `bigint` | Y |  | KEY,PHI? |
| `Patient` | `varchar(89)` | Y |  | PHI? |
| `Billing_Provider` | `varchar(183)` | Y |  |  |
| `Supervising_Provider` | `varchar(183)` | Y |  |  |
| `Ordering_Provider_Name` | `varchar(183)` | Y |  |  |
| `Sales_Rep_Name` | `varchar(104)` | Y |  |  |
| `Referring_Provider` | `varchar(75)` | Y |  |  |
| `Location` | `varchar(100)` | Y |  |  |
| `DOS` | `date` | Y |  | DATE |
| `Payer` | `varchar(100)` | Y |  |  |
| `Sales_Rep_Id` | `bigint` | Y |  | KEY |
| `Supervising_Provider_Claim` | `bigint` | Y |  |  |
| `Ordering_Provider` | `bigint` | Y |  |  |
| `Deposit_Date` | `date` | Y |  | DATE |
| `Payment_Description` | `varchar(401)` | Y |  | MEASURE |
| `Units` | `decimal(9,2)` | Y |  | MEASURE |
| `Payment_Method` | `varchar(100)` | Y |  | MEASURE |
| `Payment_Source` | `varchar(50)` | Y |  | MEASURE |
| `Entered_By` | `varchar(133)` | Y |  |  |
| `Entered_Date` | `datetime` | Y |  | DATE |
| `Posting_Date` | `datetime` | Y |  | DATE |
| `Responsible_Party` | `varchar(30)` | Y |  |  |
| `Procedure_code` | `varchar(5)` | Y |  | KEY |
| `Sequence_Number` | `bigint` | Y |  |  |
| `Revenue_Code` | `varchar(50)` | Y |  | KEY |
| `Unapplied` | `int` | Y |  |  |
| `Amount_Approved` | `money` | Y |  | MEASURE |
| `Amount_Paid` | `money` | Y |  | MEASURE |
| `Amount_Adjusted` | `money` | Y |  | MEASURE |
| `Withheld_Amount` | `money` | Y |  | MEASURE |
| `CoInsurance` | `money` | Y |  | MEASURE |
| `CoPayment` | `money` | Y |  | MEASURE |
| `Deductible` | `money` | Y |  | MEASURE |
| `Check_Number` | `varchar(150)` | Y |  |  |
| `Actual_Check_Date` | `date` | Y |  | DATE |
| `Check_Date` | `date` | Y |  | DATE |
| `Batch_Date` | `date` | Y |  | DATE |
| `Billed_Amount` | `money` | Y |  | MEASURE |
| `Entered_Date_by_month` | `nvarchar(4000)` | Y |  | DATE |
| `DOS_Date_by_month` | `nvarchar(4000)` | Y |  | DATE |
| `Check_Date_by_month` | `nvarchar(4000)` | Y |  | DATE |
| `Batch_Date_by_month` | `date` | Y |  | DATE |
| `Modifier1` | `varchar(2)` | Y |  |  |
| `Modifier2` | `varchar(2)` | Y |  |  |
| `Modifier3` | `varchar(2)` | Y |  |  |
| `Modifier4` | `varchar(2)` | Y |  |  |
| `Adjustment_Reason` | `varchar(200)` | Y |  | MEASURE |
| `BWC_Check_Amount` | `money` | Y |  | MEASURE |
| `Payment_Check_Amount` | `money` | Y |  | MEASURE |
| `Payment_Faciltiy` | `varchar(300)` | Y |  | MEASURE |
| `Comments` | `varchar(200)` | Y |  |  |
| `Batch_wise_checks_id` | `bigint` | Y |  | KEY |
| `Is_Rectified` | `bit` | Y |  | FLAG |
| `Rectified_Entry_ID` | `bigint` | Y |  | KEY |
| `Modified_Date` | `datetime` | Y |  | DATE |
| `Row_Version` | `varbinary(8000)` | Y |  |  |
| `Client_Bill` | `bit` | N |  | FLAG |

### `MAX_GlobalKPIClaimPaymentDenial`

Fact/report, 11 columns, PK: none

| Column | Type | Null | Identity | Role tags |
|---|---|---|---|---|
| `practice_code` | `bigint` | Y |  | KEY |
| `claim_no` | `bigint` | Y |  | KEY |
| `Ins_Payer_Id` | `bigint` | Y |  | KEY |
| `Insurance_Type` | `varchar(20)` | Y |  |  |
| `DOP` | `date` | Y |  | DATE |
| `copay` | `money` | Y |  | MEASURE |
| `coinsurance` | `money` | Y |  | MEASURE |
| `Deductible` | `money` | Y |  | MEASURE |
| `Combined_Remarks` | `varchar(35)` | Y |  |  |
| `Combined_Adjustments` | `varchar(71)` | Y |  | MEASURE |
| `Submission_Date_Time` | `datetime` | Y |  | DATE |
