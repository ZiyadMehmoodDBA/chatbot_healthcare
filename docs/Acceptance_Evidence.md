# Acceptance evidence (design document section 20)

Each criterion maps to automated tests (`tests/AiChatbot.Tests/Acceptance.ConversationTests.cs`, names start with the
criterion number) and to the code that enforces it. Status: **Met** = enforced and tested in code; **Partly** = code
done, needs an operator or owner step; **Open** = not built.

| # | Criterion | Status | Tests | Enforced by / what is left |
|---|-----------|--------|-------|----------------------------|
| 1 | No patient statement from a name, account or patient id alone | Met | AC01 (names, DOB, Analyst/Executive with an account number) | Patient lookup is a server flow by account number, roles BillingStaff/RcmManager only, never a model tool |
| 2 | Browser field or prompt cannot change identity or scope | Met | AC02 (model-supplied scope args ignored; two users, two scopes) | Actor only from `IActorProvider`; scope applied by `ReportBroker` as bound parameters |
| 3 | Every report has an approved definition, date basis, filters | Partly | `MetricRegistryTests`, `ReportingTests` | Date basis and filters ship with every result. Reports stay **Draft** until the owner signs DEC-01..10 |
| 4 | Exact figures from the reporting service, not model arithmetic | Met | AC04 (digits, number words, percentages withheld) | `ResponseFinalizer`: model figures not from a report are replaced; magnitude words now flagged |
| 5 | No LLM-generated SQL | Met | AC05 (hostile dates, topN) + `ReportingTests` | Fixed parameterized queries; arguments typed and bounded |
| 6 | Tool calls validated, authorized, bounded, audited | Met | AC06 | `ToolSpec` contract, plan-offered tools only, row cap, audit incl. refusals |
| 7 | Multi-agent keeps one user scope | Met | AC07 | One `ChatTurn` actor for every tool of the plan |
| 8 | Session memory cannot cross users | Met | AC08, `SessionPersistenceTests` | `ConversationScope` hash of user + conversation id; same key in the durable store |
| 9 | No PHI to unapproved external model; logs minimized | Met (code) | AC09 | Placeholders for external models; trace/audit hold no question text or account numbers. Vendor review and DPA are the organisation's step |
| 10 | Golden tests: accuracy, permissions, ambiguity, injection, failures, model changes | Partly | `Golden/coordinator.json` (routing), AC suite, `SafetyTests` | No live-model golden run (needs an approved model and fixtures); PHI evaluation set is a step-14 owner item |
| 11 | Trace a request by correlation id | Met | AC11 | `ChatTrace` line + `ReportAudit` lines share `corr`; plan, tool and llm steps |
| 12 | Tokens and cost tracked; budgets stop runaway use | Met | AC12 | `UsageMeter` (`Usage:*` settings, per user per UTC day); tokens/cost in `ChatTrace`. Prices are operator settings (0 = no cost shown). Budget is per process (single instance) |
| 13 | Exports re-check permissions and record access | Met | `ReportingTests` (export audit, role check) | Export goes through `ReportBroker`; rate limited under `RateLimit:ReportsPerMinute` |
| 14 | Read-only reporting works when the model is down | Met | AC14 | Reports, comparisons, snapshot, drill-down, recap and patient lookup are decided in code |

## Edge and operations (section 15, 16)

| Item | Status |
|------|--------|
| Per-user rate limits (`RateLimit:ChatPerMinute` 30, `RateLimit:ReportsPerMinute` 20; 0 = off) | Done, live-checked (429) |
| API response headers (nosniff, DENY framing, no-store, CSP none, no-referrer) | Done, live-checked |
| WAF, API gateway, private network, TLS, secret manager, key rotation | Operator / infrastructure |
| Alerts (error rate, denied access, export volume, cost spikes) | Operator: log lines carry the fields (`ReportAudit outcome=`, `ChatTrace est_cost_usd=`) |
| Retention: audit, exports, traces, backups | Operator policy; session rows purge after `Session:RetentionDays` |
| Incident response runbook | Operator |
