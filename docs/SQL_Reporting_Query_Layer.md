# SQL Reporting Query Layer

How a question becomes a SQL query, and the rules around it. Pair with [Data_Dictionary.md](Data_Dictionary.md).

## Flow

```
User question
  -> Safe-to-share layer   tokenize identifiers ({ACCT_1}), screen text bound for an external model
  -> LLM                   chooses a tool and passes only a token / facility name
  -> ReportTools           resolves the token server-side, calls IReportDataReader
  -> SqlReportDataReader   one fixed SELECT against one reporting view, parameters bound
  -> PolicyGate            decides whether the model sees values or only {PLACEHOLDERS}
  -> Reply                 the server fills placeholders and real account numbers
```

The model never writes SQL, never names a table or view, and never sees a connection string.

## Report specifications

### Patient summary (`GetPatientSummary`)

- **Question answered:** how many admissions, when was the last visit, and how many incidents are open for one account.
- **Source:** `reporting.vw_PatientSummary`.
- **Query:** `SELECT TOP (1) Admissions, LastVisitDate, OpenIncidents FROM <view> WHERE AccountNumber = @account`
- **Parameters:** `@account bigint`, resolved from a token the user created by writing a labelled number.
- **No row:** returned as "not found", the same for a missing account and an invalid one.
- **Output:** answer text, with figures inserted by the server.
- **Validation to confirm with the data owner:** grain is one row per account; "open incident" definition;
  `LastVisitDate` meaning (admission date or discharge date).

### Facility metrics (`GetFacilityMetrics`)

- **Question answered:** readmission rate, average length of stay and incident count for one facility.
- **Source:** `reporting.vw_FacilityMetrics`.
- **Query:** `SELECT TOP (1) ReadmissionRatePct, AvgLengthOfStayDays, IncidentCount, AsOfDate FROM <view> WHERE Facility = @facility`
- **Parameters:** `@facility nvarchar(100)`, trimmed, at most 100 characters.
- **Matching:** follows the column collation (case-insensitive on the default SQL Server collation).
- **Validation to confirm:** the rate and stay definitions, the period covered, and that `AsOfDate` is a load date.

## Rules

| Rule | How it is enforced |
|---|---|
| Read only | The reader issues `SELECT` only. Use a login with `SELECT` on the two views and nothing else. |
| No SQL from the model | Query text is constant; only validated view names are interpolated (`^[A-Za-z_]\w*(\.[A-Za-z_]\w*){0,2}$`), checked at startup. |
| Bound parameters | Account and facility are always parameters. Injection strings return "not found" (tested). |
| Failure is not "no data" | A database error becomes `unavailable` to the model, never `not_found`. The log holds the exception type only. |
| Bounded | 10 second command timeout (`Reports:CommandTimeoutSeconds`), one row, 4 tool calls per question. |
| No identifiers in logs | Account numbers, facility names and result values are not logged. |

## Configuration

| Key | Default | Notes |
|---|---|---|
| `Reports:Provider` | `Synthetic` | `Synthetic` (demo data) or `Sql`. |
| `ConnectionStrings:Reporting` | none | Required for `Sql`. User-secrets or environment, never a repo file. |
| `Reports:PatientSummaryView` | `reporting.vw_PatientSummary` | |
| `Reports:FacilityMetricsView` | `reporting.vw_FacilityMetrics` | |
| `Reports:CommandTimeoutSeconds` | `10` | |
| `Reports:AcknowledgeNoUserAuth` | `false` | `Sql` outside Development refuses to start until this is `true`: the API has no user authentication, so any caller can read any account. |

## Connect from the settings screen

Gear icon, **Database** tab. Pick **SQL Server**, enter server and database, choose Windows or SQL login, then
**Test connection** before **Save**. Saving applies immediately; no restart.

- **Test connection** checks, in order: the login works, both views exist and have the required columns, and
  whether the login can change data (a warning, not a failure: use a read-only login).
- **Messages** are fixed and plain (login failed, database not found, server did not respond, certificate not
  trusted, view not found, no permission). They never include the password or raw driver text.
- **Password** is encrypted at rest with ASP.NET Data Protection (DPAPI-backed on Windows) in
  `database-settings.json` next to the app (git-ignored) and is never sent back to the browser. A saved password is
  reused only while the server and user name are unchanged. If the keys are lost the password reads as empty and
  must be re-entered.
- **Precedence:** saved settings win; with nothing saved, `Reports:Provider` and `ConnectionStrings:Reporting` decide.
  Delete `database-settings.json` to return to configuration.
- **Who can use it:** the same protection as the other settings: loopback only, or `X-Admin-Key` when `Admin:ApiKey`
  is set. Server names get the same checks as provider URLs (no link-local or metadata hosts).
- **Outside Development** a saved SQL source is blocked until `Reports:AcknowledgeNoUserAuth=true`.
- For LocalDB or a server without a trusted certificate, untick **Encrypt** (development only).

API: `GET /api/settings/database`, `PUT /api/settings/database`, `POST /api/settings/database/test`.

## Try it locally

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -i scripts\dev-reporting-db.sql
cd src\AiChatbot.Api
dotnet user-secrets set "Reports:Provider" "Sql"
dotnet user-secrets set "ConnectionStrings:Reporting" "Server=(localdb)\MSSQLLocalDB;Database=AiChatbotReporting;Trusted_Connection=True;Encrypt=False"
dotnet run
```

Sample accounts: `12345`, `20456`, `30001`, `40002`, `50003`. Facilities: `north`, `south`, `east`, `west`.
Switch back with `dotnet user-secrets set "Reports:Provider" "Synthetic"`.

Integration tests: set `AICHATBOT_TEST_SQL` to the same connection string and run `dotnet test`; without it those
tests are skipped.

## Before real data

1. Data owner writes the two views over real tables and confirms the definitions above.
2. A read-only login limited to those views; connection over TLS (drop `Encrypt=False`).
3. User authentication and per-user scoping (which accounts and facilities a user may see). Until then the SQL
   provider is blocked outside Development.
4. Written data-policy approval, and a business associate agreement if patient data may reach an external model.
