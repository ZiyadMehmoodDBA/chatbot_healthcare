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
- Reports: every run goes through `ReportBroker` (actor + practice scope, role check, audit). Caller text never enters SQL; add a report = catalog entry + fixed query + tests. Actor comes only from `IActorProvider` (dev config today; real OIDC pending).
- Chat (`/api/chat`) requires a signed-in actor. The model only picks one of three report tools and dates; tables reach the UI as `{"report":...}` stream lines straight from `ReportBroker`, never through model text. Conversation keys are per user (`ConversationScope`). External models get placeholders for figures unless `Safety:AllowAggregateToExternal=true`.
- Domains (Phase 3): tools live in `IDomainModule`s with a full `ToolSpec` contract (schema, roles, scope, limits). `Coordinator.Plan` routes each question before any model call (Single / Multi / Open / Unsupported); only the plan's tools are offered and run, capped at `Coordinator.MaxToolCalls`. Placeholders are prefixed per report (SPLIT_, PAYER_, AGING_, numbered if repeated). Tool errors are `code: message`. Add a domain = module + keywords + tests; add an unbuilt area to the coordinator's list until it exists. Modules are in-process; a real MCP transport is a later step.
- New vendor -> `Llm`; new data source -> behind `IReportDataReader`; new data-sharing rule -> `Safety`.
- Closing a gap in Architecture.md section 9: update that table.
