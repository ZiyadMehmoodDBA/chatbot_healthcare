# CEO question catalogue

What a chief executive of a healthcare billing / RCM organisation asks a chatbot that has access to the data warehouse,
mapped to a metric, a source, and the state of the code. Use it to decide what to build next and to write golden test
questions.

## 1. What the warehouse can and cannot answer

`MedcareDW_Dev` is a **billing and revenue-cycle** warehouse (43 tables, see `MedcareDW_Dev_Data_Dictionary.md`). It holds
claims, charges, payments, adjustments, A/R, denials, submissions, payers, providers, practices and facilities.

It holds **no clinical outcome data**: no readmissions, length of stay, infection or incident rates, quality scores, wait
times or patient satisfaction. The earlier demo views (`vw_PatientSummary`, `vw_FacilityMetrics`) are synthetic. A CEO
asking "what is our readmission rate?" must get an honest "this system has billing data only", not a guess.

The ICD and CPT columns give **coded volume** (what was billed), not outcomes.

## 2. Status key

| Status | Meaning |
|---|---|
| BUILT | A catalogue report exists and answers it (Draft until the owner signs the definition) |
| BUILD | Source columns found; needs a report, a fixed query and tests. No new business decision beyond the usual draft caveats. |
| DEFINE | The data exists, but the formula needs a business-owner decision first (listed in section 5) |
| NONE | No source in the warehouse. The bot says so. |

Coverage warning: `FactClaimCptReport` and `FactClaimInterestReport` hold only practices 21003 and 21188 (dictionary
section 6A.1). Any answer from them must say which practices it covers.

## 3. The catalogue

Eleven themes, in the order a CEO asks them. IDs are stable; use them in tests and in the build plan.

### F. Money in: revenue and collections

| ID | CEO asks | Metric | Source | Status |
|---|---|---|---|---|
| F1 | How much did we collect last month / this quarter / this year? | Sum of insurance + patient payments, posting date | `FACT_CC_Claims_Payment_Count` | BUILT (`insurance-vs-patient-payments`) |
| F2 | How much came from insurance versus patients? | Split and percentages | same | BUILT |
| F3 | Which payers pay us the most? | Payers ranked by payments | same | BUILT (`payments-by-payer`) |
| F4 | How much did we bill (charges) last month? | Sum of charge amount, DOS | `FactClaimCount.Charge_Amount` (line), `FactCPT_KPI`, `FactClaimCptReport` (2 practices only) | BUILD |
| F5 | Are collections up or down against last month / last year? | Period over period change | F1 with two periods | BUILD (compare mechanism, section 6) |
| F6 | What is our net collection rate? | Payments / (charges - contractual adjustments) | `FactCPT_KPI`: `Insurance_Payments`, `Patient_Payments`, `Charge_Amount`, `Contractual_Adjustments` | DEFINE |
| F7 | What is our gross collection rate? | Payments / charges | same | DEFINE |
| F8 | How much did we write off or adjust? | Contractual + manual adjustments | `FactCPT_KPI`, `FactClaimCount.Amount_Adjusted` | BUILD |
| F9 | How much is withheld or sitting unapplied? | `Withheld_Amount`, `Unapplied` | `FactCPT_KPI`, `FactReportClaimPaymentDetails` | BUILD (second table is empty in dev) |
| F10 | How are payments arriving: check, EFT, card? | Payments by method and source | `FactReportClaimPaymentDetails`: `Payment_Method`, `Payment_Source` | BUILD (value list unknown) |
| F11 | What is our average reimbursement per claim? | Payments / unique claims | F1 | BUILD |
| F12 | Are we getting paid what the contract expects? | Payments vs expected / allowed | `FactCPT_KPI.Expected_Reimbursement`, `FactGlobalKPIClaimData.Allowed` | DEFINE |

### A. Cash tied up: accounts receivable

| ID | CEO asks | Metric | Source | Status |
|---|---|---|---|---|
| A1 | How much is outstanding in A/R? | Total open insurance balance | `FactDaysARbyPayer` | BUILT (`insurance-ar-aging`) |
| A2 | How old is our A/R? | Buckets 0-30 .. over 120 | same | BUILT |
| A3 | Which payers owe us the most? | Top payers by open balance | same | BUILT (aging with `topN`) |
| A4 | How much A/R is over 90 / 120 days? | Sum of old buckets, share of total | same | BUILT (read from aging rows); a dedicated share figure is BUILD |
| A5 | What are our days in A/R (DSO)? | A/R / average daily charges | A1 + F4 | DEFINE |
| A6 | Is A/R growing or shrinking? | A/R at two dates | none: tables are truncated and reloaded each sync | NONE until a snapshot table exists |
| A7 | How much patient balance is outstanding? | Sum of `Patient_Balance` | `FactCPT_KPI` | BUILD (aggregate; not the patient lookup) |
| A8 | How much will we likely never collect? | Bad debt estimate | none | NONE |

### D. Leakage: denials and rejections

| ID | CEO asks | Metric | Source | Status |
|---|---|---|---|---|
| D1 | How many claims were denied or rejected? | Distinct claims with a status record | `FactPayerRejectionReason` | BUILT (`denial-analysis`, counts) |
| D2 | Why are claims being denied? | Claims by status and reason | same | BUILT |
| D3 | Which payers deny the most? | Denials by payer | same, `Ins_Payer_Id` | BUILD (the report has a payer filter, no payer grouping) |
| D4 | What is our denial rate? | Denied claims / submitted claims | `FactFinancialClass`: `Totaldenials`, `TotalClaims`; submissions history | DEFINE (denominator and period) |
| D5 | How much money is tied up in denials? | Outstanding balance on claims with a denial status | `FactGlobalKPIClaimData.Outstanding_Amount` + status | DEFINE. Never call it "denied dollars": no such column exists. |
| D6 | How many denials did we appeal or recover? | Appeals, recoveries | none | NONE |
| D7 | Which denials are new this week? | Denials by entry date | `Fact_Frozen_Denial_Data` (production only) | NONE in dev |
| D8 | How many claims were resubmitted? | Resubmissions by type | `FactClaimResubmission` | BUILD |

### S. Speed: how fast we bill and get paid

| ID | CEO asks | Metric | Source | Status |
|---|---|---|---|---|
| S1 | How long does it take us to bill? | Average and longest days, service to first submission | `FactDaysToBill` | BUILT (`days-to-bill`) |
| S2 | Which payers or practices are slowest to bill? | Days to bill by payer, by practice | same | BUILT by payer; by practice is BUILD |
| S3 | How many claims are not yet billed? | Unbilled claim count and charges | `FactClaimCptReport.Billing_Status`, `Include_in_submission` | DEFINE (status values undocumented) |
| S4 | How long from billing to payment? | Billed date to payment date | `FactCPT_KPI`, payments | DEFINE |
| S5 | What is our clean claim rate? | First-pass accepted / submitted | `FactGlobalKPIClaimSubmissionsHistory` | DEFINE; do not build until the owner supplies a formula |
| S6 | How many claims did we submit last week? | Submissions by date | `FactGlobalKPIClaimSubmissionsHistory`, `FactClaimResubmission` | BUILD (claims submission performance) |

### V. Volume and productivity

| ID | CEO asks | Metric | Source | Status |
|---|---|---|---|---|
| V1 | How many claims did we process? | Unique claims by period | `FactClaimCount` | BUILT (`claim-volume`) |
| V2 | Which providers bill the most? | Claims and charges by rendering provider | same | BUILT |
| V3 | Which practices are growing or shrinking? | Claims by practice over time | same | BUILD (compare and trend) |
| V4 | Which procedures drive our revenue? | Charges and payments by CPT | `FactClaimCptReport`, `FactCPT_KPI`, `FactGlobalKPICPTClaimData` | BUILD (CPT performance; three overlapping tables, pick one) |
| V5 | What is our case mix? | Volume by financial class | `FactFinancialClass`, `FactClaimCount.Financial_Class` | BUILD |
| V6 | Where do we see patients? | Volume by location and place of service | `FactClaimCount.Location_Name`, `POS_Name` | BUILD |

### P. Payer performance

| ID | CEO asks | Metric | Source | Status |
|---|---|---|---|---|
| P1 | What is our payer mix? | Payments or charges by payer and by financial class | F3, `FactFinancialClass` | BUILT by payer; by class is BUILD |
| P2 | Which payer is most profitable or reliable? | Payment rate, days to pay, denial rate per payer | F6, S4, D4 per payer | DEFINE |
| P3 | Are we dependent on one payer? | Top payer share of payments | F3 | BUILD (share figure on the existing report) |
| P4 | How is Medicare doing versus commercial? | Group by payer type | `DimInsurances`, `DimFinancialClass.HCFA_Class`, `Insurance_Type` | DEFINE (Medicare mapping rule undecided) |
| P5 | Where are we paid late with interest? | Interest on claims | `FactClaimInterestReport` | BUILD (2 practices only) |

### L. Practices, facilities, providers, sales reps

| ID | CEO asks | Metric | Source | Status |
|---|---|---|---|---|
| L1 | Which practice or facility performs best? | Collections, A/R, denials, days to bill per practice | existing reports grouped by practice | BUILD (practice grouping) |
| L2 | How is each provider doing? | Claims, charges, collections per provider | `FactClaimCount`, `FactCPT_KPI` | BUILT for claims and charges; collections are BUILD |
| L3 | Which sales rep brings the most revenue? | Revenue by sales rep | `FactClaimCptReport.Sales_Rep`, `FactFacilitySalesRep`, `DimSalesRep` | BUILD. Rep birth date, address and email are PHI-like and must not be exposed. |
| L4 | How does location A compare with B? | Any metric by location | `Location_ID`, `Practice_Location_Id` | BUILD |

### T. Patients and patient responsibility

| ID | CEO asks | Metric | Source | Status |
|---|---|---|---|---|
| T1 | How much do patients owe in total? | Sum of `Patient_Balance` | `FactCPT_KPI` | BUILD (same as A7) |
| T2 | How much do patients pay at the time of service? | Copay, deductible, coinsurance collected | `FactClaimCptReport`, `FactReportClaimPaymentDetails` | BUILD |
| T3 | What does this patient owe? | One account's balance | `FactCPT_KPI` | BUILT (`patient-billing-balance`). A CEO normally does not ask it; staff do. See section 7. |

### Q. Trust, freshness and control

| ID | CEO asks | Metric | Source | Status |
|---|---|---|---|---|
| Q1 | How current is this data? | Last load date | `DataThrough` in every report's evidence | BUILT |
| Q2 | Can I trust this number? | Approval status, caveats, definition | evidence on every report | BUILT (all Draft) |
| Q3 | Which numbers are official? | Approved vs draft metrics | metric registry (`metric-registry.json`) | BUILT (registry and sign-off record; a chat answer is not built) |
| Q4 | Who looked at what? | Audit trail | `chatbot.AuditLog` | BUILT (not exposed to chat) |
| Q5 | Did the data load fully? | Row counts and last sync per table | ETL lineage | NONE (needs a freshness check; loads are not transactional) |

### X. Clinical and quality (not in this warehouse)

| ID | CEO asks | Answer today |
|---|---|---|
| X1 | Readmission rate, length of stay | NONE: billing data only |
| X2 | Incidents, infections, adverse events | NONE |
| X3 | Patient satisfaction, wait times | NONE |
| X4 | Outcomes by diagnosis or procedure | NONE. Only coded volume and charges by CPT or ICD exist. |
| X5 | Staffing, bed use, capacity | NONE |

The bot should answer X questions in one fixed sentence naming what it can answer instead, not with the generic
"I don't have information about this context."

## 4. How a CEO actually phrases it

The bot has to map loose wording to the catalogue. Every phrase below should be a golden test case.

| CEO says | Maps to |
|---|---|
| "How are we doing this month?" | Needs a clarifying step or a standard dashboard: F1, A1, D1, S1 together (Multi plan) |
| "What's our cash position?" / "How much cash did we bring in?" | F1 |
| "Where are we leaking money?" | D1, D2, F8 together |
| "What's stuck?" / "What's sitting in A/R?" | A1, A2 |
| "Who's slow to pay us?" | A3 (and S4 once defined) |
| "Who is slow to bill?" | S2 |
| "Top 5 payers" | F3 with `topN` |
| "Are we better than last quarter?" | F5 and the compare mechanism |
| "Give me the one-minute version" | The summary style: three lines, headline figure first |
| "Why did collections drop?" | Driver analysis: F1 by payer, practice and month. Out of scope until compare exists. |
| "What should I worry about?" | Fixed watch-list: A4 over 120 days, D2 top reason, S2 slowest payer |
| "Which practice should I call?" | L1 ranked, worst first |

## 5. Definitions the owner must sign before BUILD or DEFINE items are trusted

| Decision | Blocks |
|---|---|
| Which payment pair is authoritative (current choice: `InsurancePayments` + `PatientPayments`) | F1 to F3, F6, F7 |
| Net collection rate formula and whether contractual adjustments are in the denominator | F6, F7, P2 |
| DSO formula and averaging window | A5 |
| Denial versus rejection: which `Claim_Status` values | D1 to D5 |
| Denial rate denominator (submitted claims, per period) | D4 |
| Days to bill start event (DOS or date of entry) | S1, S2 |
| Billing status values, "unbilled" rule | S3 |
| Medicare and payer-type mapping | P4 |
| Whether `Practice_Code` is unique across source servers | everything cross-practice |
| Snapshot table for historical A/R | A6 |

## 6. Mechanisms the CEO questions need that do not exist yet

| Need | Why | Today |
|---|---|---|
| **Period comparison** ("vs last month", "vs same quarter last year") | Most CEO questions are about change, not a level | Reports take one date range. A follow-up can re-ask, but no change figure is computed. |
| **Named periods** (MTD, QTD, YTD, fiscal quarter) | CEOs say "QTD" | The model converts relative words; fiscal calendar is not defined |
| **Grouping choice** (by practice, by payer, by month) | "by practice", "by month" | Each report has one fixed grouping |
| **Standard executive summary** | "How are we doing?" | A Multi plan could run 4 reports; no fixed layout or watch-list |
| **Executive role** | The access matrix has BillingStaff, RcmManager, Analyst. A CEO is cross-practice and aggregate-only. | Needs a role: all practices, no patient-level lookup, no export of patient lines |
| **Metric registry** | "Which numbers are official?" | Definitions live in C# `ReportCatalog`, all Draft |
| **Honest out-of-scope reply** for clinical questions | X1 to X5 | Generic refusal today |
| **Drill-down** ("which payer drove that?") | Natural next question | Follow-up context carries the last report and period only |

## 7. Who asks what

| Role | Typical questions | Patient-level |
|---|---|---|
| CEO / executive | F, A, D, S, V, P, L, Q themes. Aggregate, comparison, trend. | Never |
| RCM manager | All of the above, plus drill-down by payer, practice, provider | Yes, own practices, audited |
| Billing staff | A/R follow-up, T3 patient balance, denials work list | Yes, own practices |
| Analyst | Aggregates and exports | No |

The CEO column explains why the patient lookup (T3) is a staff feature. A CEO question is almost always aggregate.

## 8. Build order

Ranked by how often a CEO asks, how clean the source is, and how few owner decisions block it.

| Order | Item | Why first |
|---|---|---|
| 1 | Period comparison (F5) and named periods | Unlocks the most-asked questions on the reports already built |
| 2 | Executive role and the "how are we doing" summary (Multi plan of F1, A1, D1, S1) | The first question a CEO types |
| 3 | Charges report (F4) and adjustments (F8) | Completes the revenue picture; needed for F6 and A5 |
| 4 | Grouping by practice (L1, S2, V3) | "Which practice" is the second most common question |
| 5 | Denials by payer (D3), payer share (P3) | Cheap extensions of built reports |
| 6 | Patient balance aggregate (A7, T1) | Source already mapped |
| 7 | Out-of-scope reply for clinical questions (X1 to X5) | Cheap, avoids wrong answers |
| 8 | Net collection rate, DSO, denial rate (F6, A5, D4) | High value; wait for the owner's formulas |
| 9 | CPT performance (V4), claims submission (S6), resubmissions (D8), interest (P5) | Remaining catalogue reports |
| 10 | Snapshot A/R (A6), metric registry (Q3) | Needs warehouse changes and approval |

## 9. Counts

62 questions, counted by the status that starts each row: 19 BUILT, 21 BUILD, 11 DEFINE, 11 NONE (5 of those are the
clinical X rows). Some BUILT rows are partly built (for example "by payer" built, "by practice" not); the notes say which.
