# CLAUDE.md

Chat app that answers questions about report data (patient summaries, facility metrics) with an LLM, without letting identifiers or patient-level values reach an external model. Flow: UI -> API -> Agent -> LLM. Details: [docs/Architecture.md](docs/Architecture.md).

## Design source of truth

Product is a healthcare billing / RCM / reporting assistant. Design and development follow [docs/rcm-ai-chatbot-project-architecture (1).md](<docs/rcm-ai-chatbot-project-architecture (1).md>): internal staff only, read-only first, coordinator + domain agents over MCP tools, approved report catalog (no LLM-generated SQL), deterministic financial math, per-call authorization, PHI guard, golden eval set, tracing. Work by its roadmap phases (section 19) and check section 20 acceptance criteria. Current code is the early demo slice; `docs/Architecture.md` describes what exists today.

## Structure (current code)

```
client/                 Angular 20 SPA (zoneless, standalone, signals): chat/, settings/, core/ services
src/AiChatbot.Safety/   tokenization, input screen, policy gate, response finalizer (depends on nothing)
src/AiChatbot.Agent/    ChatAgent tool loop (RCM mode when a ChatTurn with an actor is passed), Domains/ (tool contracts, ReportModule, Coordinator routing), RcmReportTools registry, ChatTurn (trace), ConversationScope; legacy ReportTools demo path
src/AiChatbot.Llm/      IChatClient per provider (OpenAI, Ollama), factory, saved LLM settings
src/AiChatbot.Reporting/ Phase 1: report catalog, ReportBroker (authz, fixed parameterized SQL, evidence, audit), CSV export
src/AiChatbot.Data/     SQL reader over two views, encrypted DB settings, connection tester
src/AiChatbot.Api/      host and composition root; /api/chat (NDJSON), /api/settings/*
data/reporting-views/   SQL views the report broker reads (aggregate-only, no patient columns)
tests/AiChatbot.Tests/  xUnit
docs/                   Architecture, Data_Dictionary, SQL_Reporting_Query_Layer
scripts/                dev-reporting-db.sql (local sample DB)
```

Dependency rule: libraries point inward to Agent/Safety contracts; only Api wires them together. Libraries know nothing about HTTP. Agent may use Reporting (which depends on nothing).

## Commands

```
dotnet test                                   # all tests
cd src/AiChatbot.Api && dotnet run            # API on http://localhost:5070
cd client && npm install && npm start         # UI on http://localhost:4200, proxies /api (Node >= 22.12)
```

OpenAI key: `dotnet user-secrets set "Llm:OpenAI:ApiKey" "sk-..."` in `src/AiChatbot.Api`, or env `Llm__OpenAI__ApiKey`.

## Rules

- Never commit `.env`, API keys, `settings.json`, `database-settings.json` (all git-ignored). Stage files by name.
- Safety rules live only in `AiChatbot.Safety`; do not move them into the UI or prompts.
- LLM never writes SQL. Reader issues fixed parameterized queries over `reporting.vw_PatientSummary` and `reporting.vw_FacilityMetrics`.
- History stores only tokenized or placeholder text, never real values.
- Provider kind (local vs external) is decided by address (loopback / `Safety:LocalHosts`), not provider name.
- DB failure maps to `unavailable`, never `not_found`.
- `/api/settings/*` stays loopback-only or `X-Admin-Key` protected; saved credentials are never returned to the browser.
- Reports: every run goes through `ReportBroker` (actor + practice scope, role check, audit). Caller text never enters SQL; add a report = catalog entry + fixed query + tests. Actor comes only from `IActorProvider`: Azure AD token + `chatbot.UserAccess` lookup (`Auth:Mode=AzureAd`, default outside Development; setup in docs/Azure_AD_Setup.md), or the fixed `Auth:DevActor` in Development only. Unknown/inactive users get 403, an unreadable access table 503, never access.
- Chat (`/api/chat`) requires a signed-in actor. The model only picks one of the report tools and dates (it gets no patient tool); tables reach the UI as `{"report":...}` stream lines straight from `ReportBroker`, never through model text. Conversation keys are per user (`ConversationScope`). External models get placeholders for figures unless `Safety:AllowAggregateToExternal=true`.
- Domains (Phase 3): tools live in `IDomainModule`s with a full `ToolSpec` contract (schema, roles, scope, limits). `Coordinator.Plan` routes each question before any model call (Single / Multi / Open / Unsupported); only the plan's tools are offered and run, capped at `Coordinator.MaxToolCalls`. Placeholders are prefixed per report (SPLIT_, PAYER_, AGING_, numbered if repeated). Tool errors are `code: message`. Add a domain = module + keywords + tests (current: payments, accounts receivable, claims, denials); add an unbuilt area to the coordinator's list until it exists. Modules are in-process; a real MCP transport is a later step.
- Conversation (docs/Conversation_Design.md): `DecisionEngine` runs before any model call: memory questions (first/last/recap/summarize/repeat), clarifying questions and the patient flow are answered in code from `ISessionMemory`. Patient billing balance (`patient-billing-balance`) is a server lookup by account number, never a model tool: the model never sees its result, history holds only `{ACCT_n}` tokens, audit holds `accountRef` (hash), and it has no generic run/export endpoint. No lookup by name. Add a conversation phrase = pattern in `ConversationIntents` + a test in `ConversationTests`.
- Grouped reports (Phase 4): `Reporting/GroupedReports.cs` defines by-practice, by-month and denials-by-payer reports as data (literal SQL fragments, same broker, scope, audit, registry Draft). Add a grouping = spec + registry entry + tool + tests; counts in `ReportingTests`/`DomainTests` change.
- Roles: BillingStaff, RcmManager, Analyst, Executive. Executive runs every aggregate report and the business snapshot (`SnapshotPlanner`, server-run comparisons, no model) but never the patient lookup. A report that fails in the snapshot is named unavailable and the rest still show. Existing databases run `data/access/003_executive_role.sql`.
- Values the model may see never carry free database text (payer or provider names): grouped reports expose only month, numeric code and known status values.
- Charges/payments/adjustments (`charges` domain, `claim-financials` view over FactClaimCount) show raw sums only. No collection rate, DSO or denial rate until the owner signs DEC-09; do not add a derived column before then.
- Question repository (docs/Question_Repository.md): `Reporting/Questions/question-catalogue.json` lists billing and clinical questions with metric, definition, standard and status. Clinical questions are `NoSource` until data exists; the Coordinator answers them in code with the reference and never a figure. Add a question = JSON entry; make one answerable = view + report + `status: Available`.
- Metrics (docs/Metric_Definitions.md): `Registry/metric-registry.json` holds the owner decisions and approval per report. `MetricRegistry.StatusFor` gives Approved only with a named approver, a date and every dependency decided; otherwise Draft. Do not flip a report to Approved in code or JSON without the owner's signed answers.
- Audit: every report run/export is logged and, when `ConnectionStrings:RcmAudit` is set (INSERT-only login, `data/access/002_chatbot_audit.sql`), queued to `chatbot.AuditLog`. The queue never blocks or fails a report; drops are counted and logged. Golden routing cases live in `tests/AiChatbot.Tests/Golden/coordinator.json`: add a case for every wrong route found.
- New vendor -> `Llm`; new data source -> behind `IReportDataReader`; new data-sharing rule -> `Safety`.
- Closing a gap in Architecture.md section 9: update that table.
