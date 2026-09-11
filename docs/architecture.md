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

## Multi-tenancy

`Tenant` is the top-level ownership boundary. Every tenant-owned entity
carries a `TenantId`, enforced via EF Core global query filters — a query
for another tenant's data simply returns nothing, not an error, at the
database-query level, not as an application-code check that could be
forgotten on a new endpoint. See `TenantIsolationTests` for the automated
proof of this, and the [Administrator Guide](administrator-guide.md) for
the platform-vs-tenant-admin split this creates.

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
| `IRemoteExecutionProvider` | **none** — see Known gaps below |

## Deployment execution topology

**This is the single most important thing to understand before relying on
this platform for a multi-server rollout.** The intended architecture is:

```
Portal VM ──┬──► Target Server 1 ──► Docker/Compose
            ├──► Target Server 2 ──► Docker/Compose
            └──► Target Server N ──► Docker/Compose
```

**What actually exists today**: `DeploymentExecutor` (the component that
runs `docker compose down`/`up` for an actual deployment) runs those
commands as a **local process on whatever host the portal's own API
container runs on** — it does not reach out to a `TargetServer` over any
network channel. `IRemoteExecutionProvider` (the abstraction meant to carry
deployment execution to a genuinely remote target server) exists and is
correctly wired into **container monitoring/control** (status, restart,
recreate), but its only implementation, `NotConfiguredRemoteExecutionProvider`,
honestly reports every target server unreachable rather than fabricating a
result — it never spawns a process. Deployment execution and container
monitoring/control therefore currently disagree about where "the target
server" actually is:

- **Deployment execution** (`docker compose up` for a real deploy):
  happens on the portal VM itself, regardless of what `TargetServer`/
  `Hostname` you configured.
- **Container monitoring/control** (status, restart, recreate from the
  UI): correctly refuses to pretend it reached a remote server it can't
  actually reach — reports "unreachable" honestly, every time, for every
  target server, since no real remote-execution mechanism (SSH, agent,
  remote Docker API over TLS) is implemented yet.

**Practical consequence**: today, deploy the portal's `api` container onto
the same host (or with local Docker access to the same host) as the
application(s) it deploys, for deployment execution to actually work.
Container status/control from the UI will show "unreachable" for every
target server until a real `IRemoteExecutionProvider` implementation is
built — this is not a bug to work around, it's the documented, current
state.

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
  login (a role/permission change takes effect on next login, not
  instantly).
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

1. **No real remote-execution mechanism** (see above) — the single biggest
   architectural gap. Blocks true multi-server deployment execution and all
   container monitoring/control.
2. **No deploy-from-Release path.** The build pipeline (Jenkins → Docker
   image → registry → `Release` record) is fully functional, but nothing
   deploys from a `Release` yet — `DeploymentExecutor`'s container-image
   branch explicitly throws "not implemented." Every application that
   deploys today uses the Legacy (prebuilt publish directory) path.
3. **Session storage is `sessionStorage`**, not an httpOnly cookie — a
   narrower exposure window than `localStorage`, but still script-readable.
4. **No key-rotation tooling** for the secret-encryption key — see the
   [Installation Guide](installation-guide.md#3-configure-environment-variables).
5. **No backup automation baked into the platform itself** — a script and
   documented schedule are provided (see the
   [Installation Guide](installation-guide.md#backup)), but nothing runs it
   for you; you wire it into your own cron/scheduler.
6. **Container monitoring has no UI yet** — the backend (permissions,
   validation, audit) is real and tested; there's no dedicated frontend
   page for it, consistent with it currently always reporting "unreachable"
   anyway (see gap 1).

None of these are silently worked around — each one fails honestly (a
clear error, or an explicit "not implemented" exception) rather than
fabricating success.
