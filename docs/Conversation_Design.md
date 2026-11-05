# Conversation design: memory, clarifying questions, patient lookup

How the bot behaves in a day-to-day conversation with a billing executive. Code: `src/AiChatbot.Agent/Conversation/`.

## Principle

Everything that can be decided without a model is decided in code, before any model call. The same input gives the same
decision, each decision is testable offline, and the patient path never passes patient data through a model.

## Decision order (`DecisionEngine.Decide`)

| Step | Trigger | Result | Model call |
|---|---|---|---|
| 1 | "never mind", "cancel" | Drop the open question | No |
| 1 | "start over", "new topic", "forget that patient" | Forget the patient in focus, the last report and the open question. The turn log stays. | No |
| 2 | "what was my first question", "last / previous question" | Quote it from session memory with the time and the outcome | No |
| 2 | "summarize our conversation", "what did we discuss", "previous conversation", "list my questions" | Numbered recap of the last 12 questions; "briefly" gives one line | No |
| 2 | "summarize that", "in short", "tl;dr" | The server-written headline of the last result | No |
| 2 | "say that again", "what was your last answer" | The last answer again | No |
| 2 | "what can you do", "help" | The reports this role may run, plus the patient lookup if the role has it | No |
| 3 | Answer to a question the bot asked | See patient flow below | No |
| 4 | Statement, patient balance, patient account, or an account number in the text | Patient flow | No |
| 5 | A period comparison ("payments this month vs last month", "denials Q1 vs Q2", "vs the same quarter last year") | Server runs the report for both periods, table with change, sentence written from the rows | No |
| 4b | "Drill into row 3" after a payer, practice or provider table | Server opens that row month by month (fixed parameterized query, actor scope) | No |
| 5a | A business snapshot ("how are we doing", "executive summary", "key metrics") | Server compares payments, claim volume, denials, A/R and days to bill for the period against the one before (default: last month vs the month before); one line per report plus tables | No |
| 6 | Everything else | Coordinator plan (Single, Multi, Open, Unsupported) and the model with its tools. Named periods in the question are turned into exact dates and given to the model. | Yes |

A side question (step 2) never loses an open question: the bot answers it, then repeats "Still open: ...".
A message that names a report area or an account is a data question, so "give me a summary of account 12345" is a
patient request, not "summarize that".

## Patient flow

Required information: **one account number**. Not a name: the bot cannot look patients up by name, and says so.

```
"I need the statement for a patient"
   -> asks: Which patient account should I look up? ...            (pending: PatientAccount)
"12345"            (bare digits are accepted as the answer)
   -> server looks up the account, table to the UI, reply written by the server
"how much does that patient owe"      -> reuses the account in focus
"now another patient's statement"     -> asks: Do you mean the account we just looked at (12345)?   (pending: ConfirmAccount)
"yes" / "no" / a new number           -> look up / ask for the number / look up the new one
"show the details" / "summary"        -> lines and totals / totals only
```

Rules:

- Several accounts in one request: taken one at a time.
- A patient is never reused silently: a reference like "that patient" reuses the one in focus; anything else asks first.
- A reply that is not a number and not a report question (a name, for example) is not accepted; the bot asks again.
- Changing the subject to a report question closes the open question.
- Roles without patient access (Analyst) get a refusal and nothing is queried.
- "No lines found" is worded the same whether the account does not exist or is outside the user's practices.
- A database failure says the data is unavailable. It is never reported as "no lines".

## Periods and comparison

`Periods` (Reporting) turns time words into exact dates: today, yesterday, this/last week, this/last month, MTD, QTD,
YTD, this/last quarter, last year, "last N days/weeks/months", "Q1 2026", "April", "April 2025", "2025". Calendar-based:
the fiscal calendar is not defined yet. "may" counts as a month only after words like in, for, of, vs.

`ComparePlanner` decides a comparison is wanted (compare, YoY, "vs" with two periods, trending/up or down) and picks the
report from the area (payments, payments by payer, A/R aging, claim volume, days to bill, denials). Rules:

- Of two named periods, the later one is "current".
- One period is compared with the period of the same shape just before it (month to previous month, month-to-date to the
  same days of last month, quarter to previous quarter). "Same period last year" / YoY compares with a year earlier.
- No period: it asks which. Unclear report: it asks which. Unsupported area: the coordinator's own reply.
- "Insurance vs patient payments" is a report name and is not read as a comparison.
- A/R is compared as the balance on the last day of each period (the source keeps no history).

`ReportBroker.CompareAsync` runs both periods through `RunAsync` (role, practice scope, limits and audit apply to each),
and `ComparisonBuilder` puts them side by side: grouping columns, then for chosen measures current, prior, change and change %.
The sentence comes from `ReportDigests.Compare`. If a period runs past the data-through date the table and the sentence
say the comparison is incomplete. Different period lengths are called out. Comparison tables have no CSV download.

## What the lookup returns

`patient-billing-balance`, from `reporting.vw_Rpt_PatientBilling` over `dbo.FactCPT_KPI`: claim lines (practice, claim,
line, service date, CPT, billed date, status, charge, insurance paid, patient paid, adjustments, patient responsibility,
patient balance) and a total row computed over every line. Capped at 200 lines, newest first.

It is a **billing balance**, not a statement. The warehouse has no statement number, delivery status or statement date.
The reply says so every time.

## PHI handling

- The lookup is a server call, not a model tool. The model gets no tool, no result and no placeholder for it.
- The table reaches the UI as a `{"report":...}` stream line, like every report.
- Model-facing history holds only the account token (`{ACCT_1}`), such as "Looked up the patient billing balance for {ACCT_1}."
- Evidence and audit rows carry `accountRef` (first 10 hex characters of SHA-256 of the number), never the number.
- The account number is a bound SQL parameter. Practice scope is always the actor's own practices.
- Patient-level report is chat-only: it is not in `GET /api/reports`, and `run` and `export` answer 404 for it.
- The view exposes `Patient_Account` as the lookup key and no name, date of birth, phone, policy number or diagnosis.

## Session memory (`ISessionMemory`)

Per user and conversation (same scoped key as history and the token vault). In-process today, behind an interface.

| Kept | Notes |
|---|---|
| Turn log | Question (tokenized), kind, answer, report digests. First turn is kept when a long session is trimmed (cap 200). |
| Open question | Waiting for an account, or for yes/no |
| Patient in focus | The token, not the number |
| Last report | Name and period, used as follow-up context for the model |

Memory is server-side only. Figures in it come from the report service, and none of it is sent to a model. The model
gets only the last report's name and period ("Earlier in this conversation you ran Payments by payer for 2026-05-01 to
2026-05-31 ...") so "same for April" works.

## Reports added with this slice

| Report | Source | Date basis | Notes |
|---|---|---|---|
| `claim-volume` | `FactClaimCount` | DOS | Unique claims, lines and charges by practice and rendering provider |
| `days-to-bill` | `FactDaysToBill` | First submission date | Overall row first, then payers. Excludes missing dates and negative gaps. |
| `denial-analysis` | `FactPayerRejectionReason` | Claim status date | Counts claims, never dollars (no denied-amount column exists). Free-text reason detail is left out. |
| `patient-billing-balance` | `FactCPT_KPI` | Current | Chat-only, patient-level |

All are **Draft**. `MedcareDW_Dev` shows 0 rows in every table (`MedcareDW_Dev_Data_Dictionary.md` section 1), so grain,
duplicates and reconciliation are unverified. The view scripts `003` to `006` in `data/reporting-views/` must be run by a
DBA and the tests in dictionary section 6.2 passed before any figure is called validated.

## Not built

- Persistent memory (lost on restart; single instance).
- Filter chips in the UI.
- LLM-based planner. Routing and conversation phrases are keyword and pattern rules, so unusual wording can miss. Add a
  golden case in `tests/AiChatbot.Tests/Golden/coordinator.json` or `ConversationTests` for each miss.
- Export of patient-level data.
- Other catalogue reports: claims submission performance, CPT performance, claim interest, facility and sales rep.
