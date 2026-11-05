# Question repository (billing and clinical)

One file holds every question the bot is meant to answer: `src/AiChatbot.Reporting/Questions/question-catalogue.json`
(embedded, loaded by `QuestionCatalogue`). 140 questions in 20 domains: 57 billing (from `CEO_Question_Catalogue.md`)
and 83 clinical. Add a question by adding an entry; no code change.

## Entry fields

| Field | Meaning |
|---|---|
| `id` | `F1`, `A3` ... billing; `C-QL-01` ... clinical (QL quality, PS safety, AT access, UV utilization, CE cost, PH population, PE experience, WF workforce, CR compliance, DC documentation, PL ancillary, PT patient-level) |
| `question`, `keywords` | CEO wording and the phrases the matcher looks for (whole words, longest wins) |
| `metric`, `definition` | What is measured and how, in plain words |
| `references` | The published standard that defines it (issuer and measure name) |
| `requiredData` | Warehouse data needed before it can be answered |
| `status` | `Available` (a report exists), `Planned`, `NeedsDefinition` (owner formula), `NoSource` (no data) |
| `reportIds` | Reports that answer it, when `Available` |
| `dataClass` | `Aggregate` or `PatientLevel` |
| `closest` | What billing data can offer meanwhile |

## Behaviour today

- A clinical question with `NoSource` is answered in code, no model call: the metric, its definition, the standard, the
  data needed, and "this system has billing data only for now". It never states a figure. Checked only when no billing
  domain matched, so billing questions are not hijacked.
- Golden routing for billing is unchanged.

## When clinical data arrives (how it answers, with references)

1. Land the data in the warehouse; add a view under `data/reporting-views/`.
2. Add a report to `ReportCatalog` (fixed parameterized query, evidence, caveats) and set the entry's `status` to
   `Available` with `reportIds`.
3. The table and its evidence reach the UI straight from `ReportBroker`. The evidence block carries the definition,
   standard and period, so every figure shows its reference. A local or an open model only writes the sentence around
   placeholders, as for billing reports; the server fills the numbers.
4. Patient-level clinical entries (`C-PT-*`) stay table-only, server lookup, never a model tool (see `Conversation_Design.md`).
5. Add golden routing cases and a report test.

## Caveats

- Standard names and issuers come from general knowledge of the published programmes (CMS, AHRQ, CDC, NCQA HEDIS,
  The Joint Commission, NHSN, HCAHPS). Version and measure year are not recorded. A clinical owner must confirm each
  before a figure is called official (`referenceNote` in the file).
- Keyword matching is literal; unusual wording misses. Add keywords for every miss.
- Clinical entries are definitions only. No clinical figure exists in this system.
