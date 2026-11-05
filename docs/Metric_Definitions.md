# Metric definitions: decisions the business owner must sign

Status: all 7 reports are **Draft**. A report becomes Approved only when every decision it depends on is signed and a named
person approves it. The record is `src/AiChatbot.Reporting/Registry/metric-registry.json`; the code reads it
(`MetricRegistry`) and reports Draft unless all conditions hold, whatever the file claims.

Evidence was read from `MedcareDW_Dev` (server 10.170.193.42) on 2026-11-05 with aggregate queries only. It is evidence,
not proof. Confirm against the source billing system.

## What the checks found

| Table | Rows (dev) | Note |
|---|---|---|
| `FACT_CC_Claims_Payment_Count` | 1,277,736 | Posting dates 2024-03-05 to 2026-06-05 |
| `FactDaysARbyPayer` | 1,283,197 | |
| `FactClaimCount` | 923,035 | 3 practices: 21109, 21217, 21199 |
| `FactPayerRejectionReason` | 318,501 | Statuses: Accepted, Rejected only |
| `FactGlobalKPIClaimSubmissionsHistory` | 1,998,693 | Candidate source for days to bill |
| `FactDaysToBill` | 0 | Empty |
| `FactCPT_KPI` | 0 | Empty: the patient lookup returns no lines |

`MedcareDW_Dev_Data_Dictionary.md` says every table has 0 rows. That is out of date for the first five tables.

## Decisions

| ID | Question | Evidence | Recommendation | Blocks |
|---|---|---|---|---|
| **DEC-01** | Which columns are the real payment amounts? | `InsurancePayments + PatientPayments` totals **362.1M**; `InsuranceAmountPaid + PatientAmountPaid` totals **53.8M** on the same rows. The second pair matches `Amount_Paid` on every row. On the sampled row, `InsurancePayments` = 717.00 = the charge, while `InsuranceAmountPaid` = 79.30. 370,392 rows have the second pair at 0 but the first above 0. Charges on these rows total 605.9M. | The second pair, once the owner confirms on a known claim that the lower amount was received | Payments reports, collections questions |
| DEC-02 | Posting date or deposit date? | Posting date is never empty. Deposit date is empty or 1900-01-01 on 588,569 rows. | Posting date | Payments reports |
| DEC-03 | Which statuses are a denial, which a rejection? | Only Accepted (278,572 claims) and Rejected (15,876). 7,473 claims have both. No remittance denial codes. | Call Rejected a rejection. Measure real denials after a remittance (835) source is loaded. | Denial report |
| DEC-04 | Days to bill: from which event to which? | Empty source table. Submission history joined to service date covers 494,075 claims, average 29.5 days, 1 negative gap. 213,194 claims have no history. `DOE` minus `DOS` averages 124.8 days, so `DOE` is not a billing date. | Service date to first submission, from submission history, showing coverage | Days-to-bill report |
| DEC-05 | Is `Practice_Code` unique across all source servers? | 3 codes in dev, no claim under two codes. Production unchecked. | Confirm with the data team | Any cross-practice report |
| DEC-06 | How to count duplicate claim lines? | 888 extra copies in 923,035 rows. Claim counts unaffected. | Count distinct lines | Claim volume |
| DEC-07 | Where is patient-level billing data loaded? | `FactCPT_KPI` is empty here | Point the view at a populated source | Patient balance |
| DEC-08 | Who signs? | The catalogue says "to be named" | One person and one backup | Everything |

## Effect today

- The payments reports carry an open-decision warning (DEC-01) in their notes and will keep it until signed. Do not quote
  their totals as collections.
- The denial report's overall row is labelled "Any status" and says it is not a count of denied claims.
- All reports return `Draft` in their evidence.

## How to sign

1. The owner answers each decision (outside the code, in writing).
2. Set the decision's `status` to `Decided`, with `decidedBy`, `decidedOn` and `answer`.
3. If the answer changes a report (for example DEC-01 or DEC-04), the report is changed, its `metricVersion` raised, and
   tests updated. Only then set that report's `approvalStatus` to `Approved`, with `approvedBy` and `approvedOn`.
4. Tests fail if a report is marked Approved while a decision it depends on is open.
