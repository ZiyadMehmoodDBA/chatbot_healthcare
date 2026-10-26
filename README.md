# AI Chatbot (.NET)

.NET port of the Bun/Express chatbot server. Same flow: **UI → API → Agent → LLM**.

```
client/               # Angular 20 UI (zoneless, standalone; AiChatbot.Client.esproj in the solution)
src/
├── AiChatbot.Api/    # ASP.NET Core minimal API: /api/health, /api/chat (NDJSON stream), /api/settings
├── AiChatbot.Agent/  # ChatAgent + conversation store (history, calls IChatClient)
└── AiChatbot.Llm/    # OpenAI / Ollama providers, settings (Microsoft.Extensions.AI)
tests/AiChatbot.Tests/
```

UI: `cd client && npm install && npm start` (http://localhost:4200, proxies `/api` to the API on 5070 via `proxy.conf.json`).
Production build: `cd client && npm run build` -> `client/dist/client/browser`. Needs Node >= 22.12.

## Run

```
cd src/AiChatbot.Api
dotnet user-secrets init
dotnet user-secrets set "Llm:OpenAI:ApiKey" "sk-..."
dotnet run            # http://localhost:5070
```

Or set env var `Llm__OpenAI__ApiKey`. Never commit keys.

## Safe-to-share layer

`AiChatbot.Safety` sits between the user and the LLM. The frontend and the prompt do none of this.

- `ITokenizationService`: labelled identifiers ("account 12345", "MRN 12345", "#12345") become `{ACCT_n}` tokens, kept per conversation in server memory. History stores tokenized text only.
- `ExternalInputScreen`: for external providers (OpenAI), a prompt with unlabelled 4+ digit numbers, emails, phone/SSN/DOB-like text, names or odd Unicode is not sent. The user gets a rephrase message and only `[withheld]` is stored. Ollama is treated as local and is not screened.
- `PolicyGate` + `DataClass`: Local sees everything. External never sees patient-level values; facility aggregates only when `Safety:AllowAggregateToExternal=true`.
- Data tools (`GetPatientSummary`, `GetFacilityMetrics` in `AiChatbot.Agent/Tools`): the model passes tokens and allow-listed names. When policy blocks a value, the model gets `{PLACEHOLDER}` names and the server fills the figures into the reply. An external answer containing a number the model was never shown is replaced by a server-built listing.
- Tool data is **synthetic** (`SyntheticReportDataReader`). Implement `IReportDataReader` for real data.

Limits: the screen is a mitigation, not de-identification (single first names, spelled-out digits, URLs pass). Token maps are in memory and lost on restart.

## API

`POST /api/chat` body `{ "prompt": "...", "conversationId": "<guid>" }`
Response `application/x-ndjson`: `{"delta":"..."}` lines, then `{"done":true,"model":"...","durationMs":123}`.
Errors mid-stream: `{"error":"..."}`. Same shape as the old Node server.

## Test

```
dotnet test
```

## Next steps

Ollama provider, `/api/settings` endpoints, DB-backed conversation store.
