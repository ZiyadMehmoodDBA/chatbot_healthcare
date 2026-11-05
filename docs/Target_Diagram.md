# Healthcare Revenue Intelligence Assistant: target diagram (draft)

Source: product spec, three layers, Edge Layer excluded for now. Colours show status against the current code:
green = built, amber = partial, grey = not built.

```mermaid
flowchart TB
    subgraph L1["01 Staff Chat UI (user experience)"]
        direction LR
        UI1["Natural language chat<br/>follow-ups, history"]
        UI2["Context filter chips<br/>practice, period, metric, comparison"]
        UI3["KPI cards, tables, charts<br/>definitions, refresh time"]
        UI4["Excel / PDF / CSV export"]
    end

    subgraph L2["02 Agent Runtime (Healthcare Intelligence Engine, one backend app)"]
        direction TB
        API["Authenticated API<br/>NDJSON stream, Entra ID SSO"]
        COORD["Coordinator Agent<br/>intent, terminology resolution,<br/>clarification, conversation context, orchestration"]
        BILL["Billing Intelligence Agent<br/>claim lifecycle, payer vs patient responsibility,<br/>posting vs service dates, adjustments, reversals"]
        PLAN["Reporting Planner + Query Planning<br/>LLM emits structured plan: metric, filters, period"]
        VALP["Deterministic plan validator<br/>checks plan vs permissions and Metric Registry"]
        DAS["Reporting Data Access Service / MCP<br/>read-only tools, fixed parameterized queries"]
        VAL["Validation Agent<br/>totals, duplicates, payer class, date bounds, freshness"]
        RESP["Response Agent<br/>business explanation, KPI cards, charts, exports"]
        GUARD["PHI and Policy Guard (outside the LLM)<br/>authn, RBAC + ABAC, practice scope,<br/>minimum necessary, masking, output filter, audit"]
        REG["Metric Registry<br/>certified metrics, formulas, dimensions,<br/>permitted filters, source mappings"]
        TERM["Business terminology map<br/>revenue, collected, write-offs, A/R ..."]
        LLM{{"Approved LLM<br/>self-hosted or approved cloud"}}
    end

    subgraph L3["03 Enterprise Data Warehouse (source of truth, SQL Server)"]
        direction TB
        SEM["Certified Semantic Layer<br/>metric definitions, payer mappings, date logic,<br/>practice permissions, row-level security"]
        subgraph DOM["Reporting domains"]
            direction LR
            D1["Revenue and<br/>Financial Performance"]
            D2["Patient Billing<br/>and Statements"]
            D3["Insurance Billing,<br/>Claims and Denials"]
            D4["A/R and<br/>Collections"]
            D5["Practice and<br/>Provider Analytics"]
        end
    end

    AUD[("Audit log<br/>user, question, plan, scope, outcome<br/>no PHI copied")]
    HIST[("Encrypted conversation store<br/>same access control as the data")]

    L1 -->|question + filters| API
    API --> COORD
    COORD <-->|resolve terms| TERM
    COORD --> BILL
    BILL --> PLAN
    PLAN <-->|structured plan only| LLM
    PLAN --> VALP
    REG --> VALP
    GUARD -->|authorize before retrieval,<br/>inject user scope| VALP
    VALP -->|approved plan| DAS
    DAS -->|scoped read-only SQL| SEM
    SEM --- DOM
    SEM -->|rows| DAS
    DAS --> VAL
    VAL --> RESP
    RESP <-->|figures as placeholders<br/>when LLM is external| LLM
    RESP --> API
    API --> L1

    GUARD -.-> AUD
    DAS -.-> AUD
    COORD -.-> HIST

    classDef built fill:#d4edda,stroke:#2e7d32,color:#111
    classDef partial fill:#fff3cd,stroke:#b58900,color:#111
    classDef todo fill:#e9ecef,stroke:#6c757d,color:#111
    class API,GUARD,AUD built
    class COORD,DAS,RESP,UI1,UI3,UI4,LLM,D3,D4 partial
    class BILL,PLAN,VALP,VAL,REG,TERM,SEM,HIST,UI2,D1,D2,D5 todo
```

## Request lifecycle

1. Question arrives with user identity.
2. Coordinator resolves intent and terminology; asks a short clarification if the term is ambiguous.
3. Guard checks practice access and detail level, outside the LLM.
4. Planner produces a structured plan; the validator checks it against permissions and the Metric Registry.
5. Data Access Service runs approved parameterized queries scoped to the user.
6. Validation Agent checks totals, duplicates, date boundaries, freshness.
7. Response Agent returns amount, definition, refresh time and optional breakdown. Next follow-up repeats steps 3-7.

## Deployment

Logical agents, one backend process at first. No microservices.
