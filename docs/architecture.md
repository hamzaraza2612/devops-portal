# Architecture

A practical map of how the system is built. For the full phase-by-phase
design history and rationale behind every decision, see `PROJECT_STATE.md`
at the repository root — this document is the condensed, current-state
reference; that one is the detailed record of how it got here.

## Layers

```
src/DevOpsPortal.Domain          Entities, enums, constants. No dependencies.
src/DevOpsPortal.Application     DTOs, service interfaces + implementations,
                                  IAppDbContext abstraction (EF Core-typed
                                  but provider-agnostic).
src/DevOpsPortal.Infrastructure  EF Core (Npgsql) AppDbContext + migrations,
                                  password hashing, JWT issuing, permission-
                                  based authorization, real provider
                                  implementations (GitLab, Jenkins, SMTP,
                                  AES-256-GCM secret encryption), startup
                                  data seeder.
src/DevOpsPortal.Api             ASP.NET Core Web API, controllers, JWT
                                  bearer auth wiring, exception-handling
                                  and rate-limiting middleware.
frontend/                        React 19 + TypeScript SPA (Vite, Tailwind
                                  v4), served by nginx which also reverse-
                                  proxies /api/* to the api container.
tests/DevOpsPortal.Tests         xUnit; EF Core InMemory provider — no
                                  external dependencies needed to run.
```

Dependencies point one direction only: Api → Infrastructure → Application →
Domain. Application never references Infrastructure or Api.

## Single-organization, per-environment authorization

There is no multi-tenancy concept anywhere in the system — the product is
single-organization. Authorization is three things on `User`: `IsAdmin`
(full access to everything), `CanApproveProduction` (an independent
"CTO" flag that lets a user approve Production promotions without being an
admin), and `UserEnvironmentAccess` (a join table recording which of
DEV/QA/UAT/PRODUCTION a given user may act on). Permission codes (the
`deployments.deploy.dev`-style strings checked via `[RequirePermission]`/
`EnsurePermissionAsync`) are unchanged from earlier phases — only what
grants them changed. See
`AppDbContextExtensions.GetRolesAndPermissionsAsync` for exactly how those
three sources synthesize a user's permission set at login, and the
[Administrator Guide](administrator-guide.md) for the operational view.

## Provider abstractions

Every external integration point is an interface with one (or zero) real
implementations behind it today — designed so a second provider is a new
class + one DI registration, never a change to the code that calls it:

| Abstraction | Real implementation(s) today |
|---|---|
| `IGitProviderClient` | `GitLabProviderClient` (GitLab REST API v4) |
| `IBuildProvider` | `JenkinsBuildProvider` (Jenkins REST API) |
| `INotificationProvider` | `EmailNotificationProvider` (SMTP) |
| `ISecretProvider` | `EncryptedSecretProvider` (AES-256-GCM at rest) |
| `IRemoteExecutionProvider` | `SshRemoteExecutionProvider` (SSH, via `Renci.SshNet`); `NotConfiguredRemoteExecutionProvider` for a `TargetServer` with no SSH credential set |

## Deployment execution topology

```
Portal VM ──┬──► Target Server 1 ──► Docker/Compose (over SSH)
            ├──► Target Server 2 ──► Docker/Compose (over SSH)
            └──► Target Server N ──► Docker/Compose (over SSH)
```

Both **deployment execution** (`docker compose down`/`up` for a real
deploy, and the container-image `pull`/`up -d` path) and **container
monitoring/control** (status, restart, stop, recreate) go through
`IRemoteExecutionProvider` over SSH to the application's configured
`TargetServer` — neither runs as a local process on the portal's own host.
A `TargetServer` needs `Hostname`, `SshUsername`, and a stored SSH
credential (password or private key, via `SshAuthMethod`) before it's
considered configured; one that isn't resolves to
`NotConfiguredRemoteExecutionProvider`, which honestly reports the server
unreachable rather than fabricating a result, and a deployment against it
fails with a clear, actionable error instead of silently running on the
portal's own host. Every dynamic value that goes into a remote command
(container names, compose file paths, env var names/values) is POSIX
shell-escaped before being interpolated; there is no free-form "run this
command" surface — only a fixed set of compose operations
(`Up`/`Down`/`DownWithVolumes`/`Restart`/`Start`/`Stop`/`Pull`) and
`docker inspect` on an already-discovered container name are reachable.

Admin → Deployment Targets → **Test Connection**
(`POST /api/target-servers/{id}/test-connection`) verifies SSH
connectivity, the authenticated remote user, OS info, and Docker/Compose
availability without ever returning the stored credential.

## Promotion / approval state machine

```
DEV:         (no promotion/approval) — explicit "Deploy to DEV" → Deployment
QA/UAT:      explicit promotion request → explicit approval → explicit
             "Deploy" → Deployment
PRODUCTION:  explicit promotion request (auto-creates a CTO approval
             record) → explicit CTO-permission approval of that SAME
             promotion → explicit "Deploy to Production" → Deployment
```

No environment ever deploys because a previous one succeeded; approving
never itself creates a `Deployment` row — only the separate, explicitly-
authorized deploy call does. See the [Deployment Guide](deployment-guide.md)
for the operational walkthrough.

## Concurrency

- **Database-enforced** (a Postgres partial unique index on
  `(ApplicationId, EnvironmentDefinitionId) WHERE Status IN (Pending,
  Queued, Running)`): at most one active deployment per application+
  environment, even under two truly concurrent requests. The application
  layer also does an up-front check for a clean, immediate error in the
  common (non-racing) case; the database index is the real backstop.
- **Background job engine**: in-process (`System.Threading.Channels`), not
  a separate worker service/queue technology. One job at a time, so a
  single stuck deployment can delay others — bounded by a configurable
  execution timeout (`Deployment:ExecutionTimeoutMinutes`, default 20) so a
  hung `docker compose` call can't stall the queue indefinitely.

## Security posture

- Bearer-token (JWT) authentication; permissions embedded in the token at
  login (a change to a user's admin flag, CTO flag, or environment access
  takes effect on next login, not instantly).
- Every mutating action re-validates permissions server-side — a hidden UI
  button is never the actual enforcement.
- Rate limiting on login (10/min/IP) and API-wide (300/min/IP) —
  ASP.NET Core's built-in limiter.
- Secrets: AES-256-GCM at rest, resolved only at deployment execution time,
  injected as process environment variables (never command-line
  arguments, never persisted, never returned by any API response except
  the explicit, separately-permissioned reveal action).
- Deployment/build logs are sanitized before storage (pattern-based
  `KEY=VALUE`/bearer-token redaction, plus literal replacement of any
  actually-resolved secret value).
- No SQL injection surface (parameterized EF Core queries throughout, zero
  raw SQL anywhere in the codebase).
- No CORS configuration needed or present — the frontend's nginx reverse-
  proxies `/api/*` so the browser only ever talks to one origin.

## Known gaps

Carried forward deliberately, not hidden — see `PROJECT_STATE.md`'s
"Production-critical gaps" section for the full detail behind each:

1. **No registry-credential entity.** `ContainerImage`-mode applications
   deploy from an immutable `Release` (see above), but authenticating
   `docker compose pull` against a private registry on the target server
   (a `docker login`/credential helper) is a deliberate, documented scope
   limit — expected to be configured out-of-band on the target server
   itself.
2. **Session storage is `sessionStorage`**, not an httpOnly cookie — a
   narrower exposure window than `localStorage`, but still script-readable.
3. **No key-rotation tooling** for the secret-encryption key — see the
   [Installation Guide](installation-guide.md#3-configure-environment-variables).
4. **No backup automation baked into the platform itself** — a script and
   documented schedule are provided (see the
   [Installation Guide](installation-guide.md#backup)), but nothing runs it
   for you; you wire it into your own cron/scheduler.

None of these are silently worked around — each one fails honestly (a
clear error, or an explicit "not implemented" exception) rather than
fabricating success.
