# Data Dictionary: reporting views

The app reads exactly two views. They are the whole data contract: the app never reads base tables, so the data
owner controls what is exposed by what the views contain. Names are configurable (`Reports:PatientSummaryView`,
`Reports:FacilityMetricsView`); the columns and types below are not.

**Status:** the contract is defined and tested against a local sample database (`scripts/dev-reporting-db.sql`,
synthetic rows). **No production source has been mapped.** Business meaning, grain and calculation rules for a real
warehouse must be confirmed by its owner before the views are used for decisions.

## `reporting.vw_PatientSummary`

Grain: **one row per patient account** (`AccountNumber` unique). Used by the `GetPatientSummary` tool.

| Column | Type | Null | Meaning | Data class |
|---|---|---|---|---|
| `AccountNumber` | `bigint` | no | Patient account, the lookup key. Must be unique in the view. | patient identifier |
| `Admissions` | `int` | no | Count of admissions for the account. | patient-level |
| `LastVisitDate` | `date` | yes | Most recent visit; NULL shows as `none`. | patient-level |
| `OpenIncidents` | `int` | no | Incidents for the account not yet closed. | patient-level |

Must not expose: names, date of birth, address, phone, email, policy or SSN-like values.

## `reporting.vw_FacilityMetrics`

Grain: **one row per facility** (`Facility` unique, case-insensitive). Used by the `GetFacilityMetrics` tool.

| Column | Type | Null | Meaning | Data class |
|---|---|---|---|---|
| `Facility` | `nvarchar(100)` | no | Facility name as users type it, for example `north`. | aggregate |
| `ReadmissionRatePct` | `decimal(5,2)` | no | Readmissions as a percentage of admissions. | aggregate |
| `AvgLengthOfStayDays` | `decimal(6,2)` | no | Average days from admission to discharge, discharged stays only. | aggregate |
| `IncidentCount` | `int` | no | Incidents recorded for the facility. | aggregate |
| `AsOfDate` | `date` | yes | Date the figures were last refreshed. Should be the ETL load date, not today. | aggregate |

## Handling rules

- **Patient-level data never reaches an external model** (OpenAI). Its values are replaced by placeholders and the
  server fills the real figures into the reply. Aggregates follow `Safety:AllowAggregateToExternal` (default off).
- **Unique keys are required.** If a view returned several rows per key, the app would take the first row. Enforce
  uniqueness in the view definition.
- **Reference implementation:** the two views in `scripts/dev-reporting-db.sql` show one way to satisfy the
  contract over simple tables. They are dev only.

## Open items for the data owner

1. Which source tables feed each view, and the definitions of admission, readmission, length of stay and incident.
2. A real `AsOfDate` (load timestamp) for freshness.
3. A read-only database login limited to `SELECT` on the two views.
4. If the target is a billing warehouse (claims, AR, payments), these two views do not apply: new tools and views
   are needed for those questions.
