# Status (2026-11-06)

## Completed
1. App running (API :5070, UI :4200); UI "Request failed (500)" diagnosed: Angular proxy with the API down.
2. Live test batch + saved logs (`test-logs/`), findings written up.
3. Slot filling: one period question for payments/claims/submissions/charges/denials, answer joined to the original question,
   split-first replies remembered, A/R needs no period, bare "May" understood as a month.
4. Chips (period, follow-ups, latest-full-month) and a scope line on every table answer (practices in access, period, date basis, status, data-through).
5. Privacy screen: a year after a time word ("for year 2026", "May 2026", "Q1 2026") passes; other 4+ digit numbers still blocked.
6. Empty / beyond-the-data results explained in code (data ends 2026-06-05); days-to-bill empty-source note with submissions chip.
7. Routing fixes: providers, claims processed, "insurance versus patients".
8. Figures written in code from the table when the model saw placeholders only: A/R over-90 and claim counts verified against the table.
9. Model hedging ("can't provide", "placeholder") replaced by the report's own sentence; figures kept out of the model's history.
10. Friendlier wording for questions, notes and refusals; model prompt asks for a colleague-like tone.
11. Help: "What questions i can ask from you?" lists questions per area with examples.
12. Bare period ("May") alone: continues the last report or asks which report; never revives a dropped question or guesses a year.
13. Docs: CLAUDE.md rules, golden routing cases, 675 unit tests (687 incl. skipped), 31 live cases in this folder.
14. Analysis engine: "why did collections decrease / A/R up / denials up / payer stopped paying", run by the server through approved reports, with calculated / inferred / unknown sections.
15. Knowledge layer: glossary (26 terms with limits and sign-off status) and 4 investigation playbooks, embedded JSON.
16. Typo tolerance before routing; clarifiers for "outstanding balances" and "how much did we bill".
17. Feedback thumbs with fixed reasons (/api/feedback); bar chart under tables (drawn from the cells).
18. Live tests: 39 cases, all pass; 724 unit tests pass.

## Pending
Needs your decision or data owner (cannot be built without it)
- Revenue definition and sign-off; DEC-01 (payments), DEC-09 (rates), DEC-10 (clean-claim).
- Payer-to-financial-class mapping: needed for payer / practice / provider filters by name.
- Days to bill source (FactDaysToBill empty); data after 2026-06-05; A/R history snapshots (needed to measure A/R movement).
- External-model aggregate sharing (Safety:AllowAggregateToExternal stays off).
- Row-level security: DBA change in the warehouse.
- Teams / BI portal channels: separate integrations, need tenant/app registration.

Not done yet
- Chart, thumbs and progress line compile and the stream is verified by curl, but I have not looked at them in a browser; check once.
- Small-cell suppression is built but off (Reporting:SmallCellMin = 0): the privacy owner must choose the threshold.
- Hypothesis/confirmed labels: nothing is claimed as confirmed yet.
- Purpose capture for patient-level lookups.
- Housekeeping: nothing committed; client bundle slightly over budget; C: nearly full; Architecture.md section 9 table not updated.
