# Phase 0 — Data Map, Metric Candidates, Access Matrix, Threat Model (DRAFT)

Status: **Draft for business-owner review.** Built from [SQL_Server_Data_Dictionary.md](SQL_Server_Data_Dictionary.md) (43 tables, names and types only) and [SQL_Reporting_Query_Layer.md](SQL_Reporting_Query_Layer.md). Follows [rcm-ai-chatbot-project-architecture (1).md](<rcm-ai-chatbot-project-architecture (1).md>) section 19, Phase 0.

Nothing here is a confirmed business rule. Every "candidate" needs sign-off before Phase 1 code treats it as authoritative.

Exit condition (Phase 0): approved data map, metric catalog, access matrix, threat model. This file is the first draft of all four.

## 1. Data map: report to candidate source

Table grain is not defined in the dictionary and most fact tables have no primary key. "Grain" below is a hypothesis to verify with a row-count / distinct-key query.

| Report (architecture section 9) | Candidate source | Key columns found | Hypothesized grain | Risks to verify |
|---|---|---|---|---|
| Insurance vs patient payments | `FACT_CC_Claims_Payment_Count` | `Posting_Date`, `Deposit_Date`, `InsurancePayments`, `PatientPayments`, `Total_Payment`, `Payer_Id`, `Claim_No`, `Practice_Code` | claim line (`Claim_No` + `Procedure_Code` + `Sequence_Number`) per payment | Two payment pairs (`InsurancePayments`/`PatientPayments` vs `InsuranceAmountPaid`/`PatientAmountPaid`); does `Total_Payment` = sum of either; duplicate rows; no PK (`Id` is identity only) |
| Payments by payer | `FACT_CC_Claims_Payment_Count`; richer alt `FactReportClaimPaymentDetails` (PK `Claim_Payment_Id`) | `Payer_Id`/`Payer_Name`, `Amount_Paid`, `Posting_Date`, `Deposit_Date`, `Payment_Source`, `Payment_Method` | payment line | Two overlapping sources; pick one authoritative. Payer fields are `Ins_Payer_Id`/`Payer` in the alt |
| Denial analysis | `FactPayerRejectionReason`, `MAX_GlobalKPIClaimPaymentDenial` | `Status_Reason`, `Claim_Status`, `Claim_Status_Date`, `Ins_Payer_Id`, `Combined_Remarks`, `Combined_Adjustments` | claim (maybe per status event) | `Claim_Status_Reason` is `varchar(max)` free text; "denial" vs "rejection" definition; no denial amount column in either table |
| Insurance AR aging | `FactDaysARbyPayer` (`Insurance_Balance`, `Billed_Date`), `Fact_DaysAR` (`Due_Amount`, `Billed_Date`), `FactClaimCptReport` (`Insurance_Balance`) | `Insurance_Balance`, `Billed_Date`, `Ins_Payer_Id` | claim line | Which is authoritative; aging date = `Billed_Date`? As-of snapshot or current only? `FactAccountReceivable.Balance` is `varchar(15)` and `Due_Amount` is `float` — do not use for money |
| Top payers by outstanding AR | same as AR aging | `Insurance_Balance` grouped by `Ins_Payer_Id` | aggregate of above | Insurance-only rule: exclude rows with zero/no insurance responsibility |
| Claim status / throughput | `FactClaimCount`, `FactGlobalKPIClaimData` | `Billing_Status`, `Claim_Status`, `Claim_No`, `DOS`, `DOE`, `First_Billed_Date` | claim line vs claim | Both carry `Patient_Name`; valid status values undocumented |
| Claims submission performance | `FactGlobalKPIClaimSubmissionsHistory`, `FactClaimResubmission`, `FactPayerRejectionReason.Submission_Date` | submission dates and status | submission event | Not yet read in detail |
| Days to bill | `FactDaysToBill` | `DOS`, `DOE`, `First_Submission_Date`, `DOSAge`, `DOEAge` | claim | Negative/null dates; is `DOSAge` days since DOS at load time (stale)? |
| CPT performance | `FactClaimCptReport`, `FactGlobalKPICPTClaimData`, `FactCPT_KPI` (99 cols) | `Procedure_Code`, `Charge_Amount`, `Insurance_Payments`, `Contractual_Adjustments`, balances | claim line | Three overlapping tables; `FactClaimCptReport` has the cleanest measure set |
| Account balance summary / statement | `FactClaimCptReport` (`Insurance_Balance`, `Patient_Balance`), `FactGlobalKPIClaimData` (`Outstanding_Amount`), `DimPatients` | `Patient_Account` is in `FactClaimCount`, `FactGlobalKPIClaimData`, `FactReportClaimPaymentDetails` only | claim / claim line | **Highest PHI risk.** Need a statement-line source that reconciles to balance |
| Claim interest | `FactClaimInterestReport` | not yet read | | |
| Facility / sales rep | `FactFacilitySalesRep`, `DimFacilitySalesRep`, `DimSalesRep` | not yet read | | |

Cross-cutting findings:
- **Scope key** available on most facts: `Practice_Code` (bigint). Also `Location_ID`/`Practice_Location_Id`, `Ins_Payer_Id`, provider ids. Use `Practice_Code` as the first tenant/facility scope column. Confirm it is the tenant boundary.
- **Date fields differ per table** (`Posting_Date`, `Deposit_Date`, `Check_Date`, `DOS`, `DOE`, `Billed_Date`, `Claim_Status_Date`). Each report must declare one date basis.
- **Money types are inconsistent:** mostly `money`, but `decimal(38,2)`, `float` (`FactAccountReceivable.Due_Amount`), and `varchar(15)` (`FactAccountReceivable.Balance`) also occur. Compute in `decimal`; never aggregate `float`/`varchar` money without an approved cast.
- **Patient-identifying columns in fact tables:** `Patient_Name`, `Patient`, `Patient Name`, `Date_of_Birth`, `Patient_Account`, `Primary_Subscriber_Number`, `Accession_No`. Reporting views for aggregate reports must not select them.
- **Overlap:** `Fact_Interg` and `Fact_Interg_Data` look like the same table with different column naming (spaces vs underscores). `FACT_CC_Claims_Payment_Count` and `FactReportClaimPaymentDetails` both hold payments.
- **Gap vs current code:** the app reads `reporting.vw_PatientSummary` / `reporting.vw_FacilityMetrics`. Neither exists in this DW. Phase 1 designs the real views over these tables.

## 2. Metric catalog: candidates needing owner decision

| Metric | Candidate definition | Open question for RCM owner |
|---|---|---|
| Insurance payments | `SUM(InsurancePayments)` | Which pair is authoritative? Are refunds negative rows? |
| Patient payments | `SUM(PatientPayments)` | Same; includes copay collected at service? |
| Combined payments | Insurance + Patient | Reconciles to `Total_Payment`? Document differences |
| Payment date basis | `Posting_Date` default | Posting vs deposit vs check date per report |
| Open insurance balance | `SUM(Insurance_Balance)` where > 0 | Exclude negatives (credits)? Include `Withheld_Amount`? |
| Aging bucket | days from `Billed_Date` to as-of date: 0-30, 31-60, 61-90, 91-120, 120+ | Bucket edges; age from DOS or billed date; as-of snapshot availability |
| Denial count | distinct `Claim_No` with denied status in `FactPayerRejectionReason` | What statuses = denial vs rejection; claim or line level |
| Denial rate | denied claims / submitted claims | Denominator; period basis |
| Days to bill | `First_Submission_Date - DOS` | Use DOS or DOE; negatives |
| Net collection rate, clean claim rate, days in AR | not defined | Do not build until owner supplies formula (architecture section 9 warns these vary) |

Each approved metric becomes a versioned entry under `contracts/reports/` (architecture section 18): owner, formula, inclusions/exclusions, date basis, grouping fields, effective version.

## 3. Access matrix (draft)

Roles are proposals; confirm with privacy/security.

| Capability | Billing staff | RCM manager | Analyst | Admin (settings) |
|---|---|---|---|---|
| Aggregate reports (payer, CPT, denial, AR) | own practices | all assigned practices | assigned practices | no |
| Patient-level statement / account lookup | own practices | assigned practices | no | no |
| Export aggregate | yes | yes | yes | no |
| Export patient-level | no | yes (audited) | no | no |
| Model / DB settings | no | no | no | yes |

Scope attributes on every request: user id, role, allowed `Practice_Code` set, (optional) location/provider/payer limits. Scope is enforced in the query broker (a mandatory `Practice_Code IN (...)` predicate), never from browser or model output.

## 4. Threat model (summary)

| Threat | Control | Phase |
|---|---|---|
| Caller reads another practice's data | Server-side practice scope on every query; authz tests | 1 |
| Name/ID typed in chat treated as identity | AuthN via OIDC; patient resolution only inside actor scope | 1-2 |
| LLM-generated SQL | Fixed views/procs only; typed parameters | 1 (already so) |
| PHI to external model | Existing Safety layer; extend input/output guard | 4 |
| Prompt injection via DB text (`Comments`, `Status_Reason`, `Claim_Status_Reason`) | Treat tool results as untrusted; never execute instructions from data | 2-3 |
| Wrong totals from duplicate rows / float money | Grain checks; decimal math; reconciliation tests | 1 |
| Over-broad export | Re-check authz and audit at export | 1 |
| Secrets in git | Existing git-safety rules | all |
| No audit trail | Audit table with actor, report, filters, row count, correlation id | 1 |

## 5. Open questions (block Phase 1 sign-off)

1. Is `Practice_Code` the tenant/facility boundary, and who maps users to practices?
2. Authoritative source per report (payments, AR, denials, CPT)? Which overlapping table wins?
3. Row grain and a business key for each chosen fact table (none have declared PKs except a few).
4. Valid values for `Billing_Status`, `Claim_Status`, `Responsible_Party`, `Payment_Source`, `Financial_Class`.
5. Is there a snapshot/as-of AR table, or only current balances?
6. Identity provider (Azure AD / other) for authentication, and how roles map.
7. Which reports are in release 1, and who signs formulas?
8. Is the live DW reachable now (`MedcareDW_Dev`) for grain/row-count checks, and with what read-only login?

## 6. Phase 1 proposal (after sign-off)

1. `contracts/reports/*.json`: versioned report + metric definitions for the first 3 reports (Insurance vs Patient Payments, Payments by Payer, Insurance AR Aging).
2. SQL reporting views (`reporting.*`) over the chosen facts, aggregate-only, no patient columns. Stored under `data/reporting-views/`.
3. New `AiChatbot.Reporting` library: report catalog, query broker (parameterized, scope predicate, row limit, timeout), evidence metadata (filters, date basis, row count, freshness, metric version).
4. Non-AI report API in `AiChatbot.Api`: `GET /api/reports`, `POST /api/reports/{id}/run`, authenticated actor + practice scope.
5. Auth (OIDC, with a dev stub), audit log, export endpoint with re-check.
6. Tests: reconciliation against fixtures, authorization (cross-practice denied), no raw SQL path.

Exit condition (Phase 1): reports accurate and access-controlled without an LLM.

## 7. Verified against MedcareDW_Dev (2026-10-29, read-only)

Dev SQL Server (address kept out of the repo); the read-only login has no write, CREATE VIEW or CREATE SCHEMA rights, and no `reporting` schema exists yet. A DBA must run `data/reporting-views/*.sql` and grant SELECT on those two views. Until then the report API can be pointed at the base tables with `Reporting:PaymentsView` / `Reporting:InsuranceArView` (dev only).

| Finding | Evidence | Effect |
|---|---|---|
| Payments source is `FACT_CC_Claims_Payment_Count`; `FactReportClaimPaymentDetails` is empty | 1,277,736 rows vs 0 rows | Alternative source dropped |
| `InsurancePayments + PatientPayments` is the authoritative pair | equals `Total_Payment` on 1,277,711 of 1,277,736 rows; the `...AmountPaid` pair matches on 325,048 | Answers open question 2 for payments |
| Posting date is the only usable date basis | `Posting_Date` never NULL; `Deposit_Date` NULL on 588,569 rows and holds 1900-01-01 | Deposit basis removed from the catalog |
| Grain is one row per payment posting on a claim line | 1 to 5+ rows per (claim, procedure, sequence); 8 exact duplicate rows | Count unique claims by `Claim_No` |
| Negative payments exist | 21,496 insurance and 224 patient rows | Included in totals; documented |
| A/R grain is the claim line for positive balances | no duplicates and no repeated claim line among 98,133 positive rows; duplicates only on zero/NULL balances | Aging filter `Insurance_Balance > 0` is safe |
| Data is stale | payments and A/R end 2026-06-05 | Evidence should show freshness; use that as-of date |
| Three practices only | 21109, 21199, 21217 | Dev scope |

Reconciliation: the report API matched independent SQL exactly for May 2026 (45,457 claims, 69,600 records, insurance 20,060,454.44, patient 26,624.05), for the top payer by combined payments, and for that payer's open A/R balance (7,857,550.93; 33,596 claims).
