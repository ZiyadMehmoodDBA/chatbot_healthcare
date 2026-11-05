# Azure AD sign-in setup

Users sign in with Microsoft (Azure AD / Entra ID). The API checks the token, then looks the user up in
`chatbot.UserAccess` for role and practices. A signed-in user who is not in the table gets **403** and no data.

## 1. Register two apps in Entra ID

**API app** (`rcm-chatbot-api`)
1. App registrations > New. Single tenant.
2. Manifest: set `requestedAccessTokenVersion` (under `api`) to `2`.
3. Expose an API: set the Application ID URI to `api://<api-client-id>`, add a scope `access_as_user`
   (admins and users can consent).

**SPA app** (`rcm-chatbot-web`)
1. App registrations > New. Single tenant. Platform: **Single-page application**.
2. Redirect URIs: `http://localhost:4200` for local use, plus the production URL.
3. API permissions: add `rcm-chatbot-api` > `access_as_user`; grant admin consent.

## 2. API configuration

| Key | Value |
|---|---|
| `Auth:Mode` | `AzureAd` (default outside Development) |
| `Auth:AzureAd:TenantId` | directory (tenant) id |
| `Auth:AzureAd:ClientId` | the **API** app's client id |
| `Auth:AzureAd:RequiredScope` | `access_as_user` (default) |
| `Auth:AccessCacheSeconds` | how long a lookup is cached (default 60, max 3600) |
| `ConnectionStrings:RcmAccess` | optional; access tables in another database. Default: the reporting connection |

Outside Development the API **refuses to start** unless these are set. `Auth:Mode=Dev` (fixed `Auth:DevActor`)
works only in Development.

## 3. Client configuration

Edit `client/public/auth-config.json` (public values, no secrets):

```json
{ "enabled": true, "tenantId": "<tenant>", "clientId": "<SPA client id>", "scope": "api://<api-client-id>/access_as_user" }
```

`enabled: false` keeps the app open for local development against `Auth:Mode=Dev`.

## 4. Access tables

A DBA runs `data/access/001_chatbot_access.sql`, then grants SELECT on schema `chatbot` to the chatbot's login.
For the durable audit trail also run `002_chatbot_audit.sql`, grant INSERT on `chatbot.AuditLog` to a separate login and set `ConnectionStrings:RcmAudit`.

Add a user (their object id is shown in the 403 message when they first try, or in Entra ID > Users):

```sql
INSERT chatbot.UserAccess (TenantId, ObjectId, DisplayName, Role)
VALUES ('<tenant>', '<user object id>', N'Jane Doe', 'RcmManager');
INSERT chatbot.UserPractice (UserId, PracticeCode) VALUES (SCOPE_IDENTITY(), 21109);
```

Roles: `BillingStaff`, `RcmManager`, `Analyst`, `Executive` (all aggregate reports and the business snapshot, no patient lookup; existing installs run `data/access/003_executive_role.sql`). Revoke: `UPDATE chatbot.UserAccess SET IsActive = 0 WHERE ObjectId = '...'`
(takes effect within `Auth:AccessCacheSeconds`).

## Audit health

`GET /api/health/audit` returns a status word only: `Healthy` (200), `Degraded` (200) or `Unhealthy` (503).
At start-up the app writes one `startup` row to `chatbot.AuditLog`; if that or any later write fails the status is
`Unhealthy` until a write succeeds. `Degraded` means entries were lost earlier, or no `RcmAudit` connection is set
(log-only). Point your monitor at it. It is separate from `/api/health` so you decide whether to pull the service out.

## Behaviour

- Token checks: signature, tenant issuer, audience, lifetime, scope, and `tid` equals the configured tenant.
- 401 no or bad token. 403 signed in but not set up (unknown, inactive, bad role). 503 table unreadable (never treated as access).
- The audit trail and conversation keys use the user's object id (no email or name).
- `/api/settings/*` is unchanged (loopback or `X-Admin-Key`).

## Not covered yet

Group-based role assignment, a self-service admin screen, token revocation faster than the cache TTL, rate limits.
