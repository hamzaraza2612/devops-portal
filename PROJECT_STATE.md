# PROJECT_STATE

## Current architecture

.NET 8 solution, clean/layered architecture:

```
src/DevOpsPortal.Domain          entities, enums, constants — no dependencies
src/DevOpsPortal.Application     DTOs, service interfaces + implementations,
                                  IAppDbContext abstraction (EF Core-typed but
                                  provider-agnostic), YamlDotNet (compose parsing)
src/DevOpsPortal.Infrastructure  EF Core (Npgsql) AppDbContext + migrations,
                                  password hashing, JWT issuing, permission-based
                                  authorization (policy provider + handler),
                                  startup data seeder
src/DevOpsPortal.Api             ASP.NET Core Web API, controllers, JWT bearer
                                  auth wiring, exception-handling middleware
tests/DevOpsPortal.Tests         xUnit; EF Core InMemory provider for service
                                  tests, no external dependencies
```

Root: `docker-compose.yml` (postgres + api), `.env.example`, `Dockerfile` at
`src/DevOpsPortal.Api/Dockerfile` (multi-stage: SDK build → aspnet runtime,
runs as the base image's built-in non-root `app` user).

## Completed phases

**Phase 1 — Core backend + database + authentication + RBAC.** Done.

**Phase 2 — Legacy Deployment Discovery & Configuration.** Done. Redefined
(per explicit instruction) from the original phase plan's "GitLab
integration + repository/application configuration" — GitLab API
integration itself (branch/commit discovery via GitLab's API, webhooks) is
deferred; `Repository` here is a plain reference row (name/url/provider),
not a live GitLab connection. No deployment *execution* — this phase is
configuration + read-only analysis only.

**Phase 3 — Deployment Engine & Environment Promotion Workflow.** Done. Adds
real deployment execution (background job engine, safe Docker Compose
invocation, health verification) and a controlled DEV → QA → UAT →
PRODUCTION promotion pipeline with per-environment explicit
approve/deploy actions and CTO-gated production approval. See dedicated
section below.

**Phase 4 — Deployment Portal UI & Operational Dashboard.** Done. Adds the
production-ready web UI (React SPA, `frontend/`) on top of the Phase 3
deployment engine. See dedicated section below.

**Phase 5 — Docker Container Monitoring & Operational Controls.** Done,
including a mid-phase architecture correction: container status/control
is `TargetServer`-aware (`IRemoteExecutionProvider`/`IContainerRuntimeProvider`),
never assumes the portal has local Docker access to a remote target
server, and honestly reports every target server unreachable until a real
secure remote-execution mechanism is built (not yet — abstraction only).
See dedicated section below.

## Current database state

PostgreSQL via EF Core migrations (`src/DevOpsPortal.Infrastructure/Persistence/Migrations`):
`InitialCreate` (Phase 1), `AddLegacyDeploymentConfiguration` (Phase 2).

Phase 1 tables: `Users`, `Roles`, `Permissions`, `UserRoles` (join),
`RolePermissions` (join), `AuditLogs`.

Phase 2 tables:
- `Repositories` — Name (unique), Url, Provider, Description, IsActive. No credentials.
- `Applications` — Name, Slug (unique, immutable after create), Description,
  DeploymentMode (LegacyFilesystem/ContainerImage), RepositoryId (nullable FK),
  SourcePath (nullable, monorepo subdirectory), IsActive.
- `EnvironmentDefinitions` — seeded reference data: DEV(0)/QA(1)/UAT(2)/PRODUCTION(3),
  IsProductionLike flag on PRODUCTION. Read-only via API, same pattern as Roles.
- `TargetServers` — Name (unique), Description, Hostname (config only, not
  contacted yet).
- `AllowedDeploymentRoots` — TargetServerId FK, RootPath, unique per
  (TargetServerId, RootPath). The security allow-list: every legacy-mode
  `ApplicationEnvironment.DeploymentRootPath` on that server must normalize
  under one of these.
- `ApplicationEnvironments` — the per-(Application, EnvironmentDefinition)
  config row (unique on that pair): TargetServerId, BranchName,
  DeploymentRootPath/PublishSubPath/BackupSubPath/BackupRetentionCount,
  ComposeFilePath/ComposeProjectName/ServiceName/ContainerName/ExternalNetworkName,
  HealthCheckType/Endpoint/IntervalSeconds/TimeoutSeconds, IsActive. All
  legacy-filesystem-specific fields are nullable (unused when DeploymentMode
  is ContainerImage — no TPH/subclassing yet, extend the same table when
  Phase 3 adds registry/image fields).

Unique indexes: `Repositories.Name`, `Applications.Slug`,
`EnvironmentDefinitions.Name`, `TargetServers.Name`,
`(AllowedDeploymentRoots.TargetServerId, RootPath)`,
`(ApplicationEnvironments.ApplicationId, EnvironmentDefinitionId)`.

Seeding: `DataSeeder` now also seeds the 4 `EnvironmentDefinitions`
(idempotent, same pattern as Roles) and the 7 new permission codes (picked
up automatically by the existing generic "seed all `PermissionCodes.All`,
grant all to ADMIN" loop — no seeder logic changes needed for this).

## Implemented features

Phase 1 (unchanged): Auth, RBAC, Users, Roles (read), Audit, Health — see
prior phase notes below if needed; not repeated here.

**Phase 2 — Applications**
- `GET/POST /api/applications`, `GET/PUT /api/applications/{id}`
  (`applications.view` / `applications.manage`). Slug is lowercase
  alphanumeric-with-hyphens, unique, **immutable after creation** (Update
  has no Slug field) — it's the stable identifier other things reference.
- `GET /api/applications/{id}/environments` — list an app's configured
  pipeline stages.
- `GET/PUT /api/applications/{id}/environments/{environmentDefinitionId}` —
  upsert the single config row for that (app, stage) pair. PUT validates:
  - `DeploymentRootPath` required for LegacyFilesystem mode; must
    normalize (no `..`, must be absolute) and fall under one of the target
    server's *active* `AllowedDeploymentRoots` (exact match or subdirectory,
    with correct `/`-boundary checking so `/mnt/data/apps2` can never match
    an allow-listed `/mnt/data/apps`).
  - `ComposeFilePath`/`PublishSubPath`/`BackupSubPath` must be safe relative
    paths (no leading `/`, no `.`/`..` segments).
  - `TargetServerId` must reference an existing, active `TargetServer`.
  - HealthCheck fields required/positive only when `HealthCheckType != None`.
  - ContainerImage-mode applications skip the filesystem-path checks (fields
    stay null).

**Phase 2 — Repositories**
- `GET/POST /api/repositories`, `GET/PUT /api/repositories/{id}`
  (`repositories.view` / `repositories.manage`). URL must be absolute
  http(s) with **no embedded userinfo credentials** (rejects
  `https://user:pass@host/...` outright) — the exact anti-pattern found in
  the legacy deploy script; real repo credentials belong in a future
  credential store, never inline in the URL.

**Phase 2 — Target servers & allowed deployment roots**
- `GET/POST /api/target-servers`, `GET/PUT /api/target-servers/{id}`,
  `POST /api/target-servers/{id}/allowed-roots`,
  `PUT /api/target-servers/{id}/allowed-roots/{rootId}`
  (`targetservers.view` / `targetservers.manage`). This is the
  server-side-enforced allow-list backing "Allowed legacy roots must remain
  configurable but security-restricted" — paths are normalized and
  traversal-checked before being stored or matched against.

**Phase 2 — Environments (reference data)**
- `GET /api/environments` (`environments.view`) — read-only, the 4 seeded
  pipeline stages.

**Phase 2 — Discovery**
- `POST /api/discovery/analyze-compose` (`applications.manage`) — takes raw
  docker-compose YAML *text the caller supplies in the request body*
  (pasted/uploaded; nothing is fetched from any server) and returns a
  structured, per-service parse: image, container_name, ports, working_dir,
  entrypoint, restart policy, extra_hosts, networks (flags
  external-network usage), and volumes — including resolving a named
  volume back to its actual host bind path via the top-level `volumes:
  driver_opts.device` (the exact pattern the legacy compose files use:
  `DmsApi-volume:/app` + `driver_opts.device: .../publish`). Environment
  variable values whose key looks secret-like (PASSWORD/SECRET/TOKEN/etc.)
  are redacted in the response. Purely computational — no filesystem,
  network, shell, or Docker access; nothing is persisted or deployed by
  this endpoint. Verified against the actual uploaded `DmsApi`
  docker-compose.yml, not just synthetic fixtures.

## Important configuration

Env vars: unchanged from Phase 1 (see `.env.example`) — Phase 2 added no
new required configuration; `AllowedDeploymentRoots` and `TargetServers`
are managed via the API/DB, not environment variables (per-tenant data,
not deployment-time config).

New package dependency: `YamlDotNet` (Application project) for compose
parsing — untyped/dynamic deserialization (`Dictionary<object,object>` /
`List<object>` tree-walking) rather than a fixed POCO schema, since
compose's `environment`/`entrypoint`/`networks`/`extra_hosts` fields all
legally take more than one shape (list or mapping, string or list).

## Known issues / deliberate Phase-1 scope cuts

- Role→permission catalog mutation is not exposed via API yet (roles are
  fixed-seeded; only user→role assignment is mutable). Add when a phase
  needs custom roles.
- No refresh tokens — single JWT with a configurable expiry
  (`Jwt:ExpiryMinutes`, default 8h). Fine for Phase 1; revisit if session
  UX needs improve.
- No frontend yet (Phase 9).

## Known issues / deliberate Phase-2 scope cuts

- **No deployment execution.** Nothing in this phase clones a repo, runs
  rsync, touches a real filesystem/Docker socket, or restarts a container.
  `ApplicationEnvironment` is pure configuration; a future phase reads it
  to drive an actual deploy.
- **No live GitLab integration.** `Repository` is a static reference
  (name/url/provider) with URL validation only — no API calls, no
  branch/commit listing, no webhooks yet. Branch selection today is just
  the free-text `ApplicationEnvironment.BranchName` field.
- **No credential storage.** Repository/target-server access secrets are
  out of scope here (later phase); Repository URLs are validated to
  *reject* embedded credentials rather than store them safely.
- **`TargetServer.Hostname` is inert** — recorded but never contacted;
  no SSH/Docker-API connectivity exists yet (that's when "discovery" could
  become live filesystem scanning instead of admin-pasted compose-file
  analysis).
- **No health-probe execution, no post-deploy permission-fix step, no
  backup-retention enforcement** — all three are stored as configuration
  (`HealthCheckType`/interval/timeout, `BackupRetentionCount`) for a later
  phase to act on; nothing runs them yet.
- **Roles get no new default permissions.** The 7 new Phase 2 permission
  codes are granted to ADMIN only (same pattern as Phase 1); DEVOPS/
  DEVELOPER/etc. still get zero permissions until a workflow/approval phase
  assigns role-appropriate defaults.
- **No delete endpoints** — Applications/Repositories/TargetServers/
  AllowedDeploymentRoots use `IsActive` soft-disable only, consistent with
  Phase 1's Users/Roles pattern.

## Legacy filesystem deployment — reference notes

The user supplied the actual `script.sh` and a representative app
`docker-compose.yml` (the real `DmsApi` example). Infra-specific values
(real IPs, internal network name, DB host) are intentionally not committed
to this file or to any test fixture — tests use a generic `SampleApi`
fixture with the same *shape*. Findings below now map directly onto the
Phase 2 domain model (mapping noted inline); deployment *execution* itself
is still not implemented (see Known issues above) — planned for a later
phase.

- **Deploy target = existing app folder**, not created by the tool → modeled
  as `ApplicationEnvironment.DeploymentRootPath`, validated against
  `TargetServer.AllowedDeploymentRoots` rather than assumed from any naming
  convention.
- **Sync, not replace**: deploy is `rsync -av` from the built/published
  source into `<app>/publish/`, **excluding** `appsettings*.json`,
  `*securesettings*.json`, `config.json` → `PublishSubPath` models the
  target subdirectory; the exclude-list itself is a future execution-time
  concern (not a config field — it was a deliberate instruction not to hardcode
  blanket excludes into config; deploy-time logic should look at what
  environment-specific config mechanism is in place then, not just port
  this exact list forward unexamined).
- **Backup = rollback artifact**: current `publish/*` copied into
  `<app>/Backups/<name>/` before sync → `BackupSubPath` +
  `BackupRetentionCount` model this; the copy/restore action itself is
  execution-phase work.
- **Restart = plain compose cycle**: `docker compose down && up -d` → maps
  to `ComposeFilePath`/`ComposeProjectName`/`ServiceName`/`ContainerName`,
  all independently configurable per "never assume folder = service =
  container name".
- **Containers join a pre-existing external Docker network** →
  `ExternalNetworkName` (nullable — only set when relevant); confirmed via
  the discovery endpoint's `usesExternalNetwork` flag on the real file.
- **Per-app extras** (extra log bind mount, `working_dir`, `TZ`,
  `extra_hosts`) → the discovery endpoint surfaces all of these from a
  pasted compose file today; they aren't yet first-class
  `ApplicationEnvironment` fields beyond what's listed above (adding
  dedicated columns for e.g. arbitrary extra bind mounts was judged
  premature before a real multi-mount use case in this phase — revisit if
  Phase 3/4 needs to *generate* compose files rather than just describe
  existing ones).
- **Post-sync permission fix** (chown/chmod to a specific group, SGID) →
  still just a documented host convention, not modeled as a field; treat as
  an optional per-target-server execution step in a later phase.
- **Script itself must NOT be shelled out to** — reinforced by this phase:
  discovery only ever *parses text supplied in a request body*; there is no
  code path anywhere in Phase 2 that runs a shell command, touches a
  filesystem path, or calls the Docker API.
- **Existing informal audit trail** (plaintext log file) → superseded by
  `AuditLogs`; every Phase 2 mutation (`repository.create/update`,
  `targetserver.create/update`, `targetserver.allowedroot.add/update`,
  `application.create/update`, `application.environment.create/update`) is
  audited.

## Important decisions

- ASP.NET Core / EF Core (Npgsql) chosen for the portal backend, matching
  the .NET application estate it manages.
- Permissions are embedded as JWT claims at login time (not re-queried per
  request), so a role/permission change takes effect on next login — acceptable
  for Phase 1; revisit if instant revocation becomes a requirement.
- `IAppDbContext` abstraction in Application (not full repository-per-entity)
  keeps Application testable via EF Core InMemory without leaking Npgsql
  specifics.
- **The Domain entity is named `ManagedApplication`, not `Application`.**
  `Application` collides with the `DevOpsPortal.Application` project's own
  namespace (C# resolves the bare identifier to the sibling namespace
  before the `using`-imported type in any file under that namespace tree —
  a real compiler ambiguity, confirmed by trying `global::`-qualification
  and a `using`-alias first, both of which still failed; renaming the type
  was the only clean fix). All DTOs/routes/JSON still say "application" —
  only the C# class name differs.
- **Discovery = parse admin-supplied text, not live filesystem/SSH
  scanning.** No remote-execution agent exists yet, so "discovery" in this
  phase means: an admin pastes an existing docker-compose.yml and gets back
  a structured, validated preview to fill in `ApplicationEnvironment`
  fields correctly (e.g. resolving a named volume to its real host path).
  This satisfies "read-only, never auto-deploys" trivially (it's just
  parsing a string) while still being genuinely useful. Live server
  discovery is a natural extension once a target-server connection exists.
- **ApplicationEnvironment is one flat, nullable-heavy table**, not
  split/subclassed by DeploymentMode. Simpler for two modes; revisit (e.g.
  table-per-hierarchy or a JSON column) only if Phase 3's container-image
  fields make the flat table unwieldy.
- **Path safety is a pure string-manipulation utility**
  (`DeploymentPathValidator`, Application/Common) with no filesystem
  access — deliberately testable without touching disk, and reused for
  both the absolute allow-list check and the relative-path safety check.

## Phase 3 — Deployment Engine & Environment Promotion Workflow

### Architecture

New building blocks, layered the same way as Phase 1/2:

- **Background job engine**: `IDeploymentJobQueue` (Application abstraction)
  backed by `InMemoryDeploymentJobQueue` (`System.Threading.Channels.Channel<Guid>`)
  and `DeploymentWorker : BackgroundService`, both in
  `Infrastructure/Deployments`. Enqueuing a deployment ID never blocks the
  HTTP request; the worker dequeues and calls `IDeploymentExecutor.ExecuteAsync`
  in a fresh DI scope per job. In-process only for this phase (documented
  limitation below), not a separate worker container/queue technology.
- **`IDeploymentExecutor`/`DeploymentExecutor`** (Application/Infrastructure
  split: interface + orchestration in Application, real I/O in
  Infrastructure via injected abstractions): validate → re-check no
  concurrent run → compose down (if configured) → compose up → health
  check → finalize (Succeeded only if health check passes or is skipped by
  config; otherwise Failed with a reason). Writes sequential
  `DeploymentLogEntry` rows as it goes, sanitizing every line through
  `LogSanitizer` before persisting.
- **`IComposeCommandExecutor`/`ComposeCommandExecutor`**: the only place
  that shells out. Uses `System.Diagnostics.Process` with `ArgumentList`
  (never a concatenated shell string), and only accepts a fixed
  `ComposeOperation` enum (`Down`, `DownWithVolumes`, `Up`) plus a working
  directory and compose file name — no caller-supplied arguments, no
  arbitrary commands.
- **`IHealthCheckProbe`/`HealthCheckProbe`**: real HTTP GET (2xx/3xx =
  healthy) or raw TCP-connect probing, `HealthCheckType.None` skips
  probing entirely (deployment is Succeeded once compose up exits 0).
- **`IGitProviderClient`/`GitLabProviderClient`**: read-only "latest/recent
  commit" lookup against the GitLab REST API v4, behind an interface so a
  future provider (GitHub, Bitbucket) or Jenkins-driven build system needs
  no rewrite of calling code. Never throws for expected failure modes
  (network/auth/404/unsupported provider) — always returns a
  `GitProviderResult<T>.Fail(...)`, so a GitLab outage can never break
  deployment-request creation. Optional per-repository bearer token is
  read from an **environment variable named by** `Repository.AccessTokenEnvVarName`
  at call time — never stored in, or returned by, the DB/API.
- **`IEmailSender`/`SmtpEmailSender`**: CTO production-approval
  notifications. Gracefully no-ops (logs a warning, returns `false`) when
  SMTP isn't configured rather than throwing — the portal is fully
  functional (approvals just aren't emailed) without SMTP env vars set.
- **`IDeploymentService`/`DeploymentService`**: the state machine and the
  single place all Phase 3 authorization and business rules live. Static,
  environment-independent permissions (e.g. `deployments.rollback`) are
  still enforced the Phase 1/2 way via `[RequirePermission]` on the
  controller action; environment-*dependent* permissions (promote/approve/
  deploy into QA vs. UAT vs. PRODUCTION require different permission
  codes, decided by which environment the request targets) are resolved
  and enforced **inside the service** via small
  `Dictionary<environmentName, permissionCode>` lookups + `ForbiddenException`
  → mapped to HTTP 403 by `ExceptionHandlingMiddleware`. This is a
  deliberate new pattern alongside the existing static-attribute one —
  documented here so a future phase doesn't reintroduce the static
  attribute for a case it can't express.
- **`IBuildConfigurationService`/`BuildConfigurationService`**: Mode B
  (build-from-source) foundation. Stores project/solution path, publish
  configuration, optional Dockerfile path, registry/image-name, and
  `ImageTagStrategy` per application. Validates paths the same way Phase 2
  validates deployment paths (relative, no `..`, no leading `/`). **Stores
  configuration only — nothing is built or executed from it in this
  phase**; that's the explicit foundation for a later Jenkins-based (or
  other) build system to consume without any rewrite of this table or its
  API.

### State machine (per environment)

```
DEV:         (no promotion/approval) —— explicit "Deploy to DEV" ——> Deployment
QA/UAT:      explicit promotion request → explicit approval → explicit "Deploy" ——> Deployment
PRODUCTION:  explicit promotion request (auto-creates a ProductionApproval
             record + emails whoever currently holds deployments.approve.production)
             → explicit CTO-permission approval of that SAME promotion
             → explicit "Deploy to Production" ——> Deployment
```

Hard invariants (all unit- and live-verified — see Testing below):
- **No environment ever deploys because a previous one succeeded.** Every
  environment requires its own explicit deploy call; `RequestPromotionAsync`
  and `ApprovePromotionAsync` never themselves create a `Deployment` row —
  only `DeployToDevAsync`/`DeployApprovedPromotionAsync`/`RollbackAsync` do,
  and each is a distinct, separately-authorized API call.
- **Approval never auto-triggers deployment**, for QA/UAT *or* Production —
  approving only flips `PromotionRequest.Status`/`ProductionApproval.Status`
  to `Approved`; a `Deployment` row is created only by the subsequent,
  separately-authorized `deploy` call, which itself re-checks approval
  status server-side.
- **Production cannot deploy without CTO (permission-holder) approval**:
  `DeployApprovedPromotionAsync` throws `ValidationException` unless
  `PromotionRequest.Status == Approved` **and**, for a production-like
  target, `ProductionApproval.Status == Approved` too.
- A promotion can only be requested from a **Succeeded** deployment in the
  immediately preceding environment (`SourceDeploymentId` is re-validated
  server-side against `Status == Succeeded`, never trusted from the client).
- Only one pending promotion per (application, target environment) at a
  time; only one active (Pending/Queued/Running) deployment per
  (application, environment) at a time — the latter is enforced at the
  database level (see below), not just in application code.

### Rollback

`RollbackAsync` creates a new `Deployment` row (`IsRollback = true`,
`RollbackOfDeploymentId` set) targeting the same commit as a previously
**Succeeded** deployment of that application+environment, going through
the exact same executor, concurrency guard, and audit path as a normal
deploy — no special-cased, less-safe code path for rollback.

### Concurrency & safety

- **DB-enforced** (works even against two API processes / two Postgres
  connections, not just single-process locking): a partial unique index
  `IX_Deployments_ApplicationId_EnvironmentDefinitionId` on
  `(ApplicationId, EnvironmentDefinitionId)` `WHERE "Status" IN (0,1,2)`
  (Pending/Queued/Running) — verified live against a real Postgres 16
  instance (`\d "Deployments"` shows the filtered index). This is the
  highest-severity race (duplicate concurrent deployments to the same
  target) and it cannot happen even under a lost application-level check.
- **Application-level, load-check-mutate** for the promotion/approval
  decision race (double-click "Approve"): `TryTransitionPromotionAsync`/
  `TryTransitionProductionApprovalAsync` re-query with a `Status ==
  PendingApproval` filter immediately before mutating, and return `false`
  (surfaced as `ConflictException` → 409) rather than double-deciding if
  another request already won. This is a **documented, deliberate
  tradeoff**: `ExecuteUpdateAsync`/`ExecuteDeleteAsync` (which would give a
  true atomic conditional update) compiles fine but is **not supported by
  the EF Core InMemory provider used by the test suite**, so it was
  abandoned in favor of a portable pattern; the lower blast-radius of this
  race (worst case: a promotion approved/rejected twice, caught and
  rejected on the second attempt) versus the duplicate-deployment race
  (which has the real DB backstop) made this an acceptable tradeoff rather
  than adding a Postgres-only code path.
- Duplicate-exact-commit and different-commit-already-deploying are both
  distinguished in the conflict error message.

### Security decisions

- **No arbitrary shell/Docker commands ever originate from user input.**
  `ComposeCommandExecutor` takes a fixed `ComposeOperation` enum value —
  there is no code path from any controller/DTO to a caller-supplied
  command string. `Process.ArgumentList` is used throughout (never a
  concatenated/shell-interpreted string), eliminating command injection.
- **No arbitrary filesystem paths from users** — deployment execution only
  ever uses `ApplicationEnvironment.DeploymentRootPath`, which Phase 2's
  validation already pins to one of that target server's
  `AllowedDeploymentRoots`; Phase 3 adds no new user-suppliable path input.
- **No arbitrary Git repositories** — `IGitProviderClient` only ever reads
  `Repository.Url`/`Provider` as already configured (and permission-gated)
  by Phase 2; there is no "fetch this repo" endpoint that takes a raw URL.
- **No plaintext credential storage, none returned via API.**
  `Repository.AccessTokenEnvVarName` stores only the *name* of an
  environment variable (validated `^[A-Z][A-Z0-9_]{2,99}$`) — the actual
  GitLab token lives in the container's environment, is read only at the
  moment of an outbound API call, and is never written to the database,
  logged, or included in any DTO. Same pattern for SMTP credentials
  (`Smtp:Password` — process env var / `appsettings`, never DB-stored,
  never returned).
- **No secrets in logs.** Every line written to `DeploymentLogEntry` passes
  through `LogSanitizer.Sanitize()` first, which redacts both
  `KEY=value`/`KEY: value` pairs where `KEY` contains a secret-hint word
  (PASSWORD/SECRET/TOKEN/APIKEY/etc.) and bare `Bearer <token>` occurrences
  — live-verified by deliberately deploying with a compose stderr line
  containing `DB_PASSWORD=...` and confirming it never reaches the stored
  log rows.
- **No secrets in the email sent for CTO approval** — the email body
  contains only application name, commit SHA, requester username, and
  either a configured `Portal:BaseUrl`-based reference link or a bare
  approval-record reference token (an opaque correlation ID, not a
  credential or bypass mechanism — actually approving still requires an
  authenticated call from a user holding `deployments.approve.production`,
  enforced exactly like every other approval in this service; the token is
  not, by itself, sufficient to approve anything).
- **RBAC is enforced server-side only**, never trusting any
  frontend-supplied ID, role name, or "current environment" claim — see
  the state-machine section above for the environment-dependent
  permission-resolution pattern, and the CTO-notification-recipient
  lookup below for why it's **permission-based, not role-name-based**.
- **No secrets in Git** — `appsettings.json`'s new `Smtp` section ships
  with all-empty placeholder values; `docker-compose.yml` requires
  `POSTGRES_PASSWORD`/`JWT_SIGNING_KEY` with **no default value** (`${VAR:?required}`
  syntax — `docker compose config` fails loudly if they're unset, rather
  than silently running with a weak/empty secret).

### CTO approval — design notes

`CreateProductionApprovalAsync` resolves who to email by **permission**,
not by role name: it queries active users whose role(s) grant
`deployments.approve.production`, not `WHERE Role.Name == "CTO"`. This
keeps the product generic per the explicit "Techbey is only the first
customer" requirement — a tenant can rename the role, or grant that single
permission to a differently-named or multiple roles, without any code
change. If no active user currently holds the permission, the approval
record is still created (production stays correctly blocked) and a
warning is logged server-side — the promotion is never silently lost.

There is no separate public "click this link to approve" endpoint — by
design. The email's reference token is a correlation ID for the human
reading the email, not a bypass credential; approving is always the same
authenticated, permission-checked `POST /api/promotions/{id}/approve`
call every other environment uses. A GET on `PromotionRequestDto`
(`RequiresProductionApproval`, `ProductionApprovalStatus`,
`CtoEmailSentAt` fields) gives any authorized caller (or a future UI) the
current approval state without a dedicated "check CTO approval" route.

### Database changes

New migration `AddDeploymentEngine` (validated with `dotnet ef database
update` against a fresh Postgres 16 instance, and
`dotnet ef migrations has-pending-model-changes` confirms the EF model and
migration history are in sync):

- `Deployments` — ApplicationId/EnvironmentDefinitionId/
  ApplicationEnvironmentId (FKs), CommitSha/CommitMessage/CommitAuthor/
  Branch, ImageReference/VersionLabel (Mode B, nullable), Status
  (Pending/Queued/Running/Succeeded/Failed/Cancelled), IsRollback +
  RollbackOfDeploymentId (self-FK), PromotionRequestId (nullable FK, null
  for direct DEV deploys and rollbacks), RequestedByUserId,
  RequestedAt/StartedAt/CompletedAt, FailureReason, HealthCheckPassed/
  HealthCheckDetail. Partial unique index (see Concurrency above) plus
  supporting non-unique indexes for status/commit lookups.
- `DeploymentLogEntries` — DeploymentId (FK, cascade delete), Sequence
  (unique per DeploymentId), Timestamp, Level (Info/Warning/Error),
  Message (already sanitized before insert). Row-per-log-line, so it can
  be moved to a centralized logging store later by changing only the
  write side.
- `PromotionRequests` — ApplicationId, From/ToEnvironmentDefinitionId,
  SourceDeploymentId, CommitSha (denormalized at request time), Status
  (PendingApproval/Approved/Rejected), RequestedByUserId/RequestedAt,
  DecidedByUserId/DecidedAt/DecisionNotes.
- `ProductionApprovals` — PromotionRequestId (unique FK — exactly one per
  promotion), ApprovalToken (random 32-byte hex, unique), Status,
  DecidedByUserId/DecidedAt/DecisionNotes, EmailRecipients (comma-joined,
  for audit — not a secret), EmailSentAt (nullable — null if SMTP wasn't
  configured or send failed).
- `BuildConfigurations` — ApplicationId (unique FK — one row per app),
  ProjectOrSolutionPath, PublishConfiguration, DockerfilePath (nullable),
  ImageRegistry/ImageName (nullable), ImageTagStrategy. Config-only, per
  Mode B scope.
- `ApplicationEnvironments` gained `UseDownWithVolumesOnDeploy` (bool,
  default false) — must be explicitly opted into per (app, environment);
  never a global/implicit behavior, per the explicit "`docker compose down
  -v` only where configured, never globally" requirement.
- `Repositories` gained `AccessTokenEnvVarName` (nullable string) — see
  Security decisions above.
- 12 new permission codes under `deployments.*` (view/deploy.dev/
  promote.{qa,uat,production}/approve.{qa,uat,production}/deploy.{qa,uat,production}/
  rollback), seeded like every other `PermissionCodes.All` entry. Unlike
  Phase 2 (which granted new codes to ADMIN only), Phase 3 also grants
  **role-appropriate defaults** to the seeded DEVELOPER/QA/UAT/DEVOPS/CTO
  roles (see live-verified table below) so the workflow is usable
  out-of-the-box without a manual permission-grant step per tenant.

No existing table's semantics changed; no data-destructive migration.

### Role → default permission verification (live, against seeded data)

Confirmed via `GET /api/roles` against a freshly-seeded database:

| Role | Phase 3 permissions granted |
|---|---|
| DEVELOPER | `deployments.deploy.dev`, `deployments.promote.qa` |
| QA | `deployments.approve.qa`, `deployments.deploy.qa` |
| UAT | `deployments.approve.uat`, `deployments.deploy.uat` |
| DEVOPS | `deployments.deploy.{dev,qa,uat,production}`, `deployments.promote.{qa,uat,production}`, `deployments.approve.{qa,uat}`, `deployments.rollback` |
| CTO | `deployments.approve.production` only (deliberately **not** `deploy.production` — approving and deploying to production are always two different people/actions unless the same human also holds DEVOPS-level permissions) |
| ADMIN | all of the above (superset, as in Phase 1/2) |

### API surface

- `GET/POST /api/applications/{id}/deployments/dev` — DEV is the only
  direct-deploy entry point (no promotion gate).
- `GET /api/applications/{id}/environments/{envId}/status` — current
  Succeeded deployment + latest deployment (any status) + any pending
  promotion into that environment, in one call.
- `GET /api/applications/{id}/environments/{envId}/commits/latest`,
  `.../commits/recent` — read-only GitLab lookup (see IGitProviderClient).
- `POST /api/applications/{id}/environments/{envId}/promotions` — request
  promotion into QA/UAT/PRODUCTION (permission resolved by target
  environment; auto-creates the linked `ProductionApproval` when the
  target is production-like).
- `POST /api/applications/{id}/environments/{envId}/rollback`.
- `GET /api/promotions` (pending, filterable by application/target
  environment), `GET /api/promotions/{id}`, `POST .../approve`,
  `POST .../reject`, `POST .../deploy` — approve/reject/deploy are always
  three distinct calls; deploy re-validates approval status server-side.
- `GET /api/deployments` (filterable), `GET /api/deployments/{id}`,
  `GET /api/deployments/{id}/logs` (sanitized, sequence-ordered).
- `GET/PUT /api/applications/{id}/build-configuration` — Mode B config
  only (`applications.manage`, same permission as other app config).

Every non-static-attribute endpoint above enforces its environment-
dependent permission inside `DeploymentService`, and every endpoint
re-validates referenced IDs (application, environment, promotion, source/
target deployment) against the database rather than trusting anything
else in the request — live-verified (see Testing).

### Testing

**Automated**: 154 tests total (up from Phase 1+2's prior count), all
passing (`dotnet test`, zero filter, zero failures). New Phase 3 coverage:
`DeploymentServiceTests` (32 tests — the full state-machine/RBAC/
concurrency/rollback checklist), `DeploymentExecutorTests` (6 — compose
success/failure, health-check failure marks Failed not Succeeded, log
sanitization, sequential log ordering, already-running skip),
`ComposeCommandExecutorTests`, `HealthCheckProbeTests` (8, real loopback
HTTP/TCP servers, no mocking of the network layer), `LogSanitizerTests`
(8), `BuildConfigurationServiceTests` (5), `GitLabProviderClientTests` (4,
non-network-dependent paths: invalid URL, empty project path, blank
branch, unreachable host — all graceful `Fail`, never a thrown exception).

**Live/manual verification** (real Postgres 16, real `dotnet run` API
process, real HTTP calls, no mocks) performed this phase, all confirmed:
- Migration applies cleanly to a fresh database; partial unique index
  present exactly as designed (`\d "Deployments"` inspected directly).
- `dotnet ef migrations has-pending-model-changes` → none.
- `docker compose config` validates; `POSTGRES_PASSWORD`/`JWT_SIGNING_KEY`
  correctly have no default (compose refuses to render without them).
- Full DEV → QA → UAT → PRODUCTION pipeline driven through the real API:
  DEV deploy request returns immediately (HTTP call doesn't block on the
  background worker); the worker picks the job up, runs real
  `docker compose` (`ArgumentList`-based `Process`) in the configured
  directory, and correctly marks the deployment **Failed** (not falsely
  Succeeded) with a captured reason when the compose daemon is
  unreachable — proving the async job pipeline, the real (non-shell)
  process invocation, and the "never mark Succeeded on failure" rule all
  work end-to-end.
- Deployment logs persisted, sequence-numbered from 1, and a deliberately
  injected `DB_PASSWORD=...` line in simulated compose stderr never
  appears in the stored rows.
- A user holding only the DEVELOPER role's permissions: **can** deploy to
  DEV (200), **cannot** approve a QA promotion (403), **cannot** deploy a
  QA promotion (403 — `Missing required permission 'deployments.deploy.qa'`),
  and an unauthenticated request is rejected (401).
- QA promotion: approval by a QA-permission holder flips status to
  Approved; the database is queried directly afterward and confirms
  **zero** `Deployments` rows were created by the approval call itself;
  only the subsequent explicit `POST .../deploy` call creates one.
- Production promotion: requesting it auto-creates a `ProductionApproval`
  (`RequiresCtoApproval: true`, `CtoApprovalStatus: PendingApproval`);
  attempting `POST .../deploy` before that approval is decided returns
  **HTTP 400** ("has not been approved yet"); after a
  `deployments.approve.production`-holding user approves, the database is
  again queried directly and confirms **zero** deployment rows exist yet;
  only the subsequent explicit deploy call creates one (which the
  background worker then picks up exactly as DEV did).
- Full audit trail confirmed present via `GET /api/audit` for every
  lifecycle event exercised above: `deployment.requested`,
  `deployment.failed`, `promotion.requested`, `promotion.approved`,
  `production_approval.requested`, `production_approval.granted` — each
  with correct entity linkage.
- Regression: Phase 1/2 endpoints (users, roles, applications, target
  servers, allowed roots, environments) all exercised live in the same
  session without error, confirming no Phase 3 change broke existing
  functionality.

Live-verification database/state cleaned up afterward (temporary DB
dropped, temporary `/tmp` compose fixture directories removed) — nothing
from this manual pass was left running or committed.

### Known limitations / explicit Phase 4+ candidates

- **No remote multi-server execution.** `ComposeCommandExecutor` runs
  `docker compose` as a **local** process. `TargetServer.Hostname` is
  still inert (as noted in Phase 2) — there is no SSH/remote-Docker-API
  connectivity or credential vault yet, so today's executor only works
  correctly when the portal process itself runs on (or has local Docker
  access to) the target host. Building real remote execution needs a
  credential store first — flagged, not started, per the explicit
  "no credential storage" Phase 3 scope cut.
- **The portal's own `docker-compose.yml` deliberately does not mount the
  Docker socket into the `api` container** — giving the main API
  unrestricted Docker access would violate this same phase's own
  "no arbitrary Docker commands" security principle at the container-boundary
  level. This means the live-verification pass in this phase (and any real
  deployment today) requires the API process to run somewhere with direct
  `docker compose` access — not automatically true when the portal itself
  runs inside its own container. This is the natural next architectural
  problem for Phase 4 (a scoped executor sidecar/agent, or the
  remote-execution credential vault above) rather than something to route
  around by loosening the portal container's own access.
- **Background job engine is in-process** (`Channel<Guid>` +
  `BackgroundService` inside the API process), not a separate worker
  process/queue technology — acceptable for this phase's scope but means a
  restart of the API process loses any not-yet-dequeued (still `Pending`,
  never got to `Queued`) job; already-`Running` jobs left in that state by
  a crash would need a reconciliation pass in a later phase (not built
  here — out of the explicit Phase 3 scope list, which excludes monitoring/
  alerting infrastructure).
- **Promotion/production-approval decision race is application-level
  (load-check-mutate), not DB-atomic** — see Concurrency & safety above
  for the full rationale; the higher-severity duplicate-deployment race
  *is* DB-enforced.
- **Mode B (build-from-source) is configuration-only** — `BuildConfiguration`
  is stored and validated but nothing ever builds or pushes an image from
  it yet; that's explicitly deferred to whatever build system (e.g.
  Jenkins) a later phase integrates, per the master requirements' own Mode
  B scope.
- Explicitly out of scope this phase (per the governing spec) and not
  built: monitoring dashboards/CPU-RAM graphs, centralized log
  aggregation, a notification center, Slack/Teams integrations, advanced
  analytics, billing, advanced user management, SSO/LDAP, Kubernetes
  deployment, and any Jenkins-specific implementation. The interfaces
  introduced this phase (`IGitProviderClient`, `IDeploymentJobQueue`,
  `IComposeCommandExecutor`, `IHealthCheckProbe`, `IEmailSender`,
  `BuildConfiguration`) are deliberately shaped so each of these can be
  added later without rewriting the deployment engine itself.

## Phase 4 — Deployment Portal UI & Operational Dashboard

Done. Adds the production-ready web UI on top of the Phase 3 deployment
engine — a React SPA (`frontend/`) that exposes the full DEV→QA→UAT→
PRODUCTION workflow, plus one small, additive backend change the UI
genuinely needed. No Phase 1/2/3 backend behavior was changed; the two
backend edits this phase are purely additive (new optional field, new
optional query parameter with a default that preserves prior behavior).

### UI architecture

```
frontend/
  src/
    api/            client.ts (fetch wrapper: bearer token, error
                     normalization, session-expiry event) + endpoints.ts
                     (one typed function per backend route)
    auth/            AuthContext (login/logout/session restore via
                     sessionStorage token) + permissions.ts (mirrors
                     PermissionCodes.cs — the single source of truth for
                     every permission string used anywhere in the UI)
    types/api.ts     Every backend DTO and enum, hand-mirrored field-for-
                     field (including exact numeric enum values, since
                     the API serializes enums as ints, camelCase props)
    components/      Layout/nav, ActionButton (API call + busy/error/
                     confirm state), Can (permission-gated render),
                     PromotionCard, StatusBadge, RouteGuards, ErrorBoundary
    pages/           One file per route (see below)
    utils/           format.ts (dates/duration), status.ts (badge
                     colors/labels), deploymentIndex.ts (client-side
                     "latest deployment per app+environment" index built
                     from one unfiltered /api/deployments fetch, avoiding
                     an N-per-application status call on list pages)
  Dockerfile         node:22-alpine build stage -> nginx:1.27-alpine
                     runtime, serving the static build
  nginx.conf         Serves the SPA (client-side routing fallback to
                     index.html) and reverse-proxies /api/* to the `api`
                     container on the internal Docker network — the
                     browser only ever talks to one origin, so no CORS
                     configuration was needed anywhere.
```

Stack: Vite + React 19 + TypeScript (strict — `erasableSyntaxOnly` means
every backend enum is a `const` object + derived union type, not a real
`enum`, since real TS enums aren't erasable), React Router 7, Tailwind
CSS v4 (compiled at build time via `@tailwindcss/vite`, not the CDN
runtime — this is a real deployed app, not a preview artifact), Vitest +
React Testing Library for tests.

**Backend authority, always.** Every permission check in the UI (the
`can()` helper, the `<Can permission=...>` component, `hasEnvironmentAccess`
for per-environment visibility) only decides what to *offer* — hiding a
button never substitutes for the corresponding server-side check, which
still runs on every request exactly as built in Phase 1–3. This is stated
directly in `auth/AuthContext.tsx`'s docstring so it can't be missed by a
future change.

### Dashboard

Real data only, no mocks: total applications, per-environment "currently
deployed" application counts (derived as: for each app+environment pair,
is the *most recent* deployment there `Succeeded`), running/succeeded/
failed deployment counts, pending-approval count, and the 8 most recent
deployments — all computed client-side from three parallel calls
(`/api/applications`, `/api/deployments`, `/api/promotions`) rather than
a dedicated aggregate endpoint (none was added; the existing list
endpoints already carry everything needed).

### Application pages

- **List** (`/applications`): search/filter by name, slug, or repository;
  a status badge per environment tier built from the same "latest
  deployment per app+environment" index the dashboard uses. Branch and
  latest-available-commit are deliberately *not* shown here (see Known
  limitations) to avoid a live GitLab call per row.
- **Details** (`/applications/:id`): application info, one card per
  environment (DEV/QA/UAT/PRODUCTION) showing branch, deployed commit,
  target server, the configured application URL (permission-gated, opens
  in a new tab), and every relevant action for that environment inline:
  Deploy to DEV (with an on-demand "look up latest available commit"
  GitLab lookup — never called eagerly), request/approve/reject/deploy
  for QA/UAT/PRODUCTION, and rollback (dropdown of prior successful
  deployments). Deployment history table below.

### Deployment actions & pending requests

The full Phase 3 state machine is exposed exactly as designed: DEV has
one action (deploy); QA/UAT/PRODUCTION each go through request → approve/
reject → deploy as three separate UI actions hitting three separate API
calls — clicking Approve never deploys anything, matching the backend
invariant. Production promotions additionally surface `RequiresCtoApproval`/
`CtoApprovalStatus` from `PromotionRequestDto`, and the Deploy button is
disabled (with an explanatory tooltip) until both the promotion and the
CTO approval are granted.

`/pending` groups by target environment in tabs (QA/UAT/PRODUCTION), and
each card shows exactly what the spec's example format requires:
application, commit, requester, request time, current status, and a
plain-language "next action" line — never a bare undifferentiated list.
A tab is hidden entirely for a user with no permission relevant to that
environment (see RBAC behavior below).

**Backend addition needed for this**: `IDeploymentService.ListPendingPromotionsAsync`
gained an optional `includeApprovedAwaitingDeploy` parameter (default
`false`, preserving the exact Phase 3 behavior for any existing caller).
Without it, a promotion disappears from every list the moment it's
approved (its `Status` leaves `PendingApproval`) — but no `Deployment` row
exists yet, so there would be no way for the UI to ever surface the
"click Deploy" step again. With the flag, the query also returns
`Status == Approved` promotions that have no linked `Deployment` row yet.
This was live-verified end-to-end: approve a QA promotion → it correctly
disappears from the *default* pending-approval query → the broadened
query still finds it and offers Deploy → clicking Deploy creates the
`Deployment` row and it disappears for good. `PromotionsController`'s
`GET /api/promotions` now accepts `includeApprovedAwaitingDeploy` as an
optional query-string boolean; all 154 backend tests (including the 32
`DeploymentServiceTests` covering this exact area) still pass unmodified.

### Deployment history, details, and logs

`/deployments` filters by application, environment, status, and date
range (all client-side over one `/api/deployments` fetch). `/deployments/:id`
shows every field `DeploymentDto` carries — commit, branch, version/image,
timestamps, duration, health-check result, failure reason, rollback
linkage — plus a logs panel backed by `GET /api/deployments/:id/logs`
(already sanitized server-side by Phase 3's `LogSanitizer`, so the UI does
no additional redaction — it only formats). Logs support manual refresh
and an auto-refresh toggle (4s interval) that's on by default while the
deployment is active and turns itself off once it completes; the
deployment record itself also polls while active so the status badge and
health/failure panel update without a manual page reload. Centralized log
aggregation was explicitly out of scope (per the spec) and was not built.

### Application URLs

New: `ApplicationEnvironment.ApplicationUrl` (nullable string, migration
`AddApplicationEnvironmentUrl`), validated with the same rule Phase 2 uses
for `Repository.Url` (absolute http/https, no embedded userinfo
credentials). Returned in `ApplicationEnvironmentDto`/accepted in
`UpsertApplicationEnvironmentRequest`. The UI renders it as a plain
`<a target="_blank" rel="noopener noreferrer">` — never a hardcoded string
anywhere in frontend code — and only when `hasEnvironmentAccess` says the
current user has some relevant permission for that specific environment
(see RBAC behavior). No new endpoint was needed; it rides along with the
existing environment-config read the details page already made.

### Environment view

`/environments`: one column per pipeline tier, each listing every active
application currently deployed there (status badge, commit, relative
time) plus a "Needs attention" highlight when that environment has a
`Failed` deployment or a pending promotion request, so it's immediately
obvious which environment needs action without reading every row.

### RBAC behavior

- Every list/detail page renders exactly what its underlying endpoint
  returns for the caller — there is no client-side application allow-list
  beyond what `applications.view` (a single, global permission, same as
  Phase 1–3) already governs; this system has no per-application ACL
  concept to enforce more finely than that.
- Per-environment gating (`hasEnvironmentAccess`, used for the pending-
  request tabs, the environment-view columns, and application-URL
  visibility) grants access to a tier if the user holds any of that
  tier's promote/approve/deploy permissions, **or** the blanket
  `deployments.view` read permission — matching the precedent Phase 3
  already established (deployment history/logs across *all* environments
  are already visible to any `deployments.view` holder; introducing a
  stricter boundary just for URLs/tabs would be a new, inconsistent
  restriction rather than an enforcement of an existing one). The
  function itself correctly discriminates a narrower permission set (unit-
  tested with synthetic permission arrays); with the *currently seeded*
  default roles, every one of them includes `deployments.view`, so in
  practice today every authenticated user sees every tab read-only — the
  actual enforcement boundary, live-verified, is on the *actions*
  (Approve/Reject/Deploy buttons), which are correctly gated per exact
  environment permission and rejected server-side with 403 if bypassed.
- Live-verified with real users against a running instance (not just
  unit tests): a DEVELOPER-role user can deploy to DEV but sees no
  Approve/Reject controls anywhere; a QA-role user sees and can use
  Approve/Reject on the QA tab. Both were confirmed via full browser
  sessions (Playwright + Chromium) against the real API, not mocked.
- Session handling: a 401 from any API call dispatches a
  `session-expired` event; `AuthContext` listens globally and clears the
  session, which routes the user back to `/login` via `RequireAuth` —
  works for a request made anywhere in the app, not just ones made
  through a specific hook.

### Error handling

Centralized in `api/client.ts`: every non-2xx response is turned into an
`ApiError` carrying the backend's own human-readable message (never a
stack trace — the backend's `ExceptionHandlingMiddleware` already
guarantees that); a network failure (fetch throwing) becomes a generic
"could not reach the server" `ApiError` instead of an unhandled rejection;
a 401 triggers the session-expired flow above. Pages render errors via a
shared `ErrorBanner` with a dismiss/retry affordance, and a top-level
`ErrorBoundary` catches any unexpected render-time exception so a bug
never shows a blank white page. "Deployment not found", "missing
configuration" (e.g. an environment not yet configured for an app), and
failed action attempts (shown inline on the specific button, via
`ActionButton`'s built-in error state) are all covered by this same
mechanism — there is no separate ad hoc error path anywhere in the app.

### Responsive layout

Tailwind responsive utilities throughout (`sm:`/`md:`/`xl:` grid-column
breakpoints on the dashboard stat row, applications table, environment
board; the top nav collapses behind a hamburger toggle below `md:`).
Desktop/laptop DevOps usage was the priority per the spec; no animation
or decorative work was done beyond what Tailwind's defaults provide for
free (hover states, transitions on interactive elements).

### Testing

Backend: all 154 existing tests still pass unmodified (confirmed after
every backend change this phase, including the new
`includeApprovedAwaitingDeploy` parameter and the `ApplicationUrl` field/
migration); one existing test file (`ApplicationEnvironmentServiceTests`)
updated only for the new positional DTO parameter, no assertions changed.

Frontend: 35 Vitest + React Testing Library tests across 8 files,
covering the explicit Phase 4 checklist:
- `auth/permissions.test.ts` — `hasEnvironmentAccess` correctly scopes a
  Developer to DEV+QA, a QA-role permission set to QA only, a UAT-role
  set to UAT only, and denies every tier to an unrelated permission set
  (the "unauthorized users cannot access restricted environments" case,
  proven with a synthetic permission array since every currently-seeded
  default role happens to include the blanket `deployments.view`).
- `pages/PendingRequestsPage.test.tsx` — a QA-only permission set sees
  only the QA tab and its request; a UAT-only set sees only UAT; a set
  with no relevant permission sees neither tab.
- `components/PromotionCard.test.tsx` — the required-format fields render
  correctly; approval/CTO-approval states are displayed and correctly
  gate the Deploy button; Approve/Reject only render for a holder of the
  matching environment's approve permission.
- `components/ActionButton.test.tsx` and `pages/ApplicationDetailsPage.test.tsx`
  — clicking an action button invokes the correct typed API function with
  the correct arguments (deploy-to-DEV verified end-to-end from typed
  commit-SHA input to the exact API call payload); a destructive action
  requires a second confirm click before calling anything; a failed call
  shows the backend's message inline instead of throwing.
- `pages/DeploymentDetailsPage.test.tsx` — logs load and render from the
  API; a 500 and a 404 both render a readable message instead of
  crashing.
- `api/client.test.ts` — the full error-handling contract: backend error
  message surfaced, network failure wrapped as `ApiError`, 401 dispatches
  the session-expired event, 204 handled as success-with-no-body, token
  storage round-trips.
- `components/Can.test.tsx` — permission-gated rendering in isolation.
- `pages/ApplicationDetailsPage.test.tsx` — the configured application URL
  renders as a real `target="_blank" rel="noopener"` link with the exact
  configured `href` (never a hardcoded frontend string) and is absent
  entirely for a user without environment access.

**Live/manual verification** (real Postgres 16, real `dotnet run` API,
real `vite dev` frontend proxying to it, Playwright + Chromium — not a
description of intended behavior, an actual browser session against the
real stack): logged in as seeded admin; created a sample application with
DEV/QA environment URLs configured; deployed to DEV, force-marked
Succeeded (no Docker daemon in this sandbox — see Phase 3's own noted
limitation); requested a QA promotion; approved it as admin; confirmed
**zero** deployment rows existed immediately after approval; clicked
Deploy from the same page and confirmed the deployment was created and
picked up by the background worker; viewed deployment history, deployment
details, and the sanitized log viewer for a real (Docker-unreachable,
correctly `Failed`, never falsely `Succeeded`) deployment; created a
DEVELOPER-role and a QA-role user and confirmed live, in the browser, that
each sees exactly the actions their role's permissions allow — no console
errors on any page throughout. All temporary databases, target-server
fixture directories, and dev-only npm packages installed for this manual
pass (Playwright, `--no-save`) were removed afterward; nothing from it was
left running or committed.

### Known limitations / explicit Phase 5+ candidates

- **No admin UI for Users/Roles/Repositories/Target Servers.** Those
  remain API-only, exactly as they were after Phase 1/2 — the Phase 4
  spec's 16 sections don't ask for management screens for them, so none
  were built, consistent with staying in scope.
- **Branch and latest-available-commit are not shown on the applications
  list page**, only on each application's details page. Showing them on
  every list row would mean a live GitLab call per application on every
  list-page load; the on-demand ("click to look up") pattern used on the
  details page's Deploy-to-DEV form was judged the right tradeoff instead.
- **Per-environment tab/URL visibility currently has no practical effect**
  beyond what `deployments.view` (already blanket, already established in
  Phase 3) grants, because every seeded default role includes it — see
  RBAC behavior above. The `hasEnvironmentAccess` function is correct and
  tested for a narrower permission set; it just isn't exercised by any
  *default* role today. Revisit if a future phase introduces a role that
  deliberately withholds `deployments.view`.
- **JWT stored in `sessionStorage`**, not an httpOnly cookie — cleared on
  tab close (narrower exposure window than `localStorage`) but still
  readable by any script on the page if an XSS existed elsewhere. An
  httpOnly-cookie-based session (requiring backend changes to issue/read
  the cookie) is the natural hardening step, not built here to avoid
  touching Phase 1's working JWT-bearer auth code.
- **No centralized log aggregation, no monitoring dashboards/CPU-RAM
  graphs, no notification center/Slack/Teams integration** — all
  explicitly out of scope per the governing spec, not built.
- **In-process background job engine, single-host Docker execution,
  application-level (not DB-atomic) promotion-decision race handling** —
  all inherited, unchanged, Phase 3 limitations; see that section above.
  Phase 4 added no new concurrency-sensitive backend logic beyond the
  purely additive query broadening described above.
- **Frontend build is type-checked together with its test files**
  (`tsc -b` covers everything under `src/`, including `*.test.tsx`) —
  intentional (catches dead code in tests too) but means a broken test
  file blocks `npm run build`, not just `npm test`. Worth splitting into a
  separate test-only tsconfig if that coupling ever becomes annoying.

## Phase 5 — Docker Container Monitoring & Operational Controls

Done, **with a mid-phase architecture correction** documented in full below
— read "Remote-execution architecture correction" first if you only read
one part of this section; it changes what "done" means here relative to
the original implementation.

Adds Docker container status/health visibility and scoped operational
controls (restart/start/stop, and a permission-gated, explicitly-opted-in
`docker compose down -v` / `up -d` recreate) for application environments
Phase 2/3 already configured. **No database schema change** — status is
read live at request time (when a real remote-execution mechanism
eventually exists) or from `Deployment` rows Phase 3 already persists;
nothing new is stored.

### Remote-execution architecture correction

**The original Phase 5 implementation was architecturally wrong and has
been replaced.** It ran `docker`/`docker compose` as a **local process**
from the portal's own machine, exactly like Phase 3's deployment
executor — but container *monitoring/control* (unlike a deploy job) was
framed as reaching "the configured application environment's containers"
in general, which is only true if the portal happens to run co-located
with every target server. **It doesn't**: the portal runs on its own VM,
separate from every `TargetServer`, per corrected requirements. The
original code would have silently inspected/controlled whatever the
portal's own container could see (almost certainly nothing) while
presenting that as if it were the real target server's status — exactly
the "pretending it works" failure mode the correction explicitly called
out.

**The fix**: container status/control now goes through a two-layer,
`TargetServer`-aware abstraction instead of ever spawning a local process
directly:

```
ContainerOperationsService
        │  (Application) — permission checks, validation, audit,
        │   DTO shaping; knows nothing about *how* Docker is reached
        ▼
IContainerRuntimeProvider
        │  (Application/Abstractions, impl:
        │   DockerComposeContainerRuntimeProvider)
        │  — Docker/Compose-domain orchestration: discover containers via
        │   `compose ps`, enrich via `docker inspect`, map state/health
        │   (ContainerStateMapper), honor the ServiceName filter. Real,
        │   tested logic — but only ever reachable through the layer below.
        ▼
IRemoteExecutionProvider
        │  (Application/Abstractions, impl today:
        │   NotConfiguredRemoteExecutionProvider)
        │  — THE boundary for "how do we reach this specific TargetServer's
        │   Docker engine". Takes a TargetServer + a fixed, allow-listed
        │   ComposeOperation (or a container name to inspect, discovered
        │   from this same target server's own `compose ps` output, never
        │   from a caller) — never a free-form command.
        ▼
   (today: nothing — always reports "not configured", never spawns a
    process, never contacts anything)
```

This matches the intended, and now explicitly documented, topology:

```
Portal VM ──┬──► Target Server 1 ──► Docker/Compose
            ├──► Target Server 2 ──► Docker/Compose
            └──► Target Server N ──► Docker/Compose
```

`NotConfiguredRemoteExecutionProvider` is the only registered
implementation of `IRemoteExecutionProvider`. `IsConfigured(targetServer)`
always returns `false`; `RunComposeAsync`/`InspectContainerAsync` always
return a clear failure naming the target server and explaining that no
remote execution mechanism is configured — **never** a fabricated
success, and never a local process spawn. This is a deliberate,
documented placeholder per the explicit instruction: "If remote Docker
connectivity cannot safely be implemented in this phase because secure
credentials/agent infrastructure is not yet available, implement the
provider abstraction and configuration boundary rather than pretending
that local Docker monitoring covers remote servers." No SSH credential
store, no per-target-server agent, and no secure way to reach an
arbitrary remote host exists anywhere in this codebase yet — building one
safely is real, non-trivial work (credential storage, host-key
verification, network reachability from the portal VM to every target
server) that a future phase should do deliberately, not as a side effect
of this correction.

**What this means concretely**: every container status/control endpoint
in this phase is real (permissions enforced, inputs validated, targets
resolved from configuration, actions audited) but currently **always**
reports the target server as unreachable — there is no code path today
that successfully inspects or controls a real remote container. That is
the honest, currently-true state of the system, not a bug to paper over.
See "What's actually operational vs. abstraction-only" below for the
precise line.

### Why Phase 3's deployment executor was deliberately NOT touched

`DeploymentExecutor`/`IComposeCommandExecutor`/`ComposeCommandExecutor`
still run `docker compose` as a local process, completely unchanged by
this correction, per the explicit instruction not to change the existing
Phase 3 deployment workflow unnecessarily. This means Phase 3's deploy
pipeline has **the same underlying local-execution limitation** this
correction fixes for Phase 5 — already honestly documented in Phase 3's
own "Known limitations" section since it was written ("today's executor
only works correctly when the portal process itself runs on, or has
local Docker access to, the target host"). That was already an honest,
if incomplete, statement — not a claim that it worked for arbitrary
remote servers. Phase 5's original mistake was introducing a *new*
capability (container monitoring/control) using that same pattern
without equally honest framing, for a use case (routine status/health
checks, not a one-time deploy) where the gap is far more visible. Fixing
*that* is this correction's job. Unifying deployment execution onto the
same `IRemoteExecutionProvider` abstraction once a real implementation
exists is a natural, sensible future step — flagged in Known limitations
below, not started here.

### `ComposeOperation` vocabulary is shared, not duplicated

`ComposeOperation` (`Up`/`Down`/`DownWithVolumes`/`Restart`/`Start`/
`Stop`/`Ps`) is the same fixed enum both `IComposeCommandExecutor` (local,
Phase 3) and `IRemoteExecutionProvider.RunComposeAsync` (target-server-
aware, Phase 5) accept — one allow-list, two independent execution paths
that both refuse anything outside it. A future real
`IRemoteExecutionProvider` implementation (SSH-based or agent-based) will
translate the same enum values into whatever it needs to run remotely
(e.g. `ssh <host> "cd <dir> && docker compose -f <file> restart"`,
built the same `Process`+`ArgumentList`-safe way, never string
concatenation) — no new vocabulary to invent, no risk of the remote path
accepting an operation the local path wouldn't.

### What's actually operational vs. abstraction-only

**Operational today** (real, tested, live-verified):
- Permission enforcement (`containers.view`/`control`/`recreate`,
  checked inside `ContainerOperationsService`, same pattern as
  `DeploymentService`).
- Target/configuration validation (`ApplicationEnvironment` must exist,
  be active, be `LegacyFilesystem` mode) — resolves and validates the
  specific `TargetServer` for every call.
- The recreate action's three independent gates (permission, the
  `UseDownWithVolumesOnDeploy` opt-in, the `Confirm: true` body flag) and
  its concurrency guard against an in-progress deployment.
- Full audit trail for every action, in both outcomes, with the target
  server named in every entry and compose output sanitized before
  storage.
- `ContainerOperationsService`'s health-check section — `IHealthCheckProbe`
  makes a real HTTP/TCP call to `ApplicationEnvironment.HealthCheckEndpoint`,
  which Phase 3 already requires to be independently network-reachable
  (not via a local socket), so it is genuinely unaffected by the portal/
  target-server separation this correction addresses.
- `DockerComposeContainerRuntimeProvider`'s orchestration and parsing
  logic (container discovery from `compose ps` output in either
  documented JSON shape, `docker inspect` enrichment, `ContainerStateMapper`
  state/health mapping, `ServiceName` filtering) — real code, fully unit-
  tested against a fake `IRemoteExecutionProvider` standing in for a
  future real one, ready to be exercised for real the moment a real
  provider is registered.

**Abstraction-only today** (the actual "reach a remote target server's
Docker engine" capability):
- `IRemoteExecutionProvider` has exactly one implementation —
  `NotConfiguredRemoteExecutionProvider` — which always reports every
  target server unreachable. No container status is ever actually
  fetched from a real host; no restart/start/stop/recreate action ever
  actually reaches a real container. Every `GET .../containers` call
  returns `IsReachable: false` with a clear `UnreachableReason`; every
  control action returns `Success: false` with the same message.
- There is no SSH credential storage, no host-key verification, no
  per-target-server agent, and no configuration surface (on `TargetServer`
  or elsewhere) for *how* to reach a given server — deliberately not
  built this phase (see the correction's own instruction above), since
  building it safely is substantial, security-sensitive work of its own
  (credential vault design, network/firewall reachability from the portal
  VM to every target server, host-key trust) that deserves to be its own
  deliberate phase, not a rushed side effect of this one.

### API surface (unchanged from the original implementation)

Same routes, same request/response shapes plus two new response fields
(`IsReachable`, `UnreachableReason`, and `TargetServerName`) on the status
endpoint — no breaking change to anything that shipped:

- `GET /api/applications/{id}/environments/{envId}/containers`
  (`containers.view`).
- `POST .../containers/restart` / `.../start` / `.../stop`
  (`containers.control`).
- `POST .../containers/recreate` — body `{ "confirm": true }`
  (`containers.recreate` + `UseDownWithVolumesOnDeploy` + confirm, all
  required).

### Permission model (unchanged)

| Role | `containers.*` permissions granted |
|---|---|
| DEVELOPER | `containers.view` |
| QA | `containers.view` |
| UAT | `containers.view` |
| DEVOPS | `containers.view`, `containers.control`, `containers.recreate` |
| CTO | `containers.view` |
| ADMIN | all three (superset, as in every prior phase) |

### Database changes

None, before or after this correction. Confirmed via
`dotnet ef migrations has-pending-model-changes` (none) both before and
after the rework, and a live `dotnet ef database update` against a fresh
Postgres 16 instance (applies only the pre-existing Phase 1–4 migrations).
Deliberately did **not** add connection/credential fields to
`TargetServer` in this correction — see Known limitations below for why
that's real, security-sensitive work left for whichever future phase
actually implements a remote-execution provider, rather than a schema
placeholder with no working behavior behind it today.

### Security decisions (updated)

- **No arbitrary Docker commands** — unchanged principle,
  now enforced at *two* layers instead of one: `ComposeOperation` is the
  fixed vocabulary both the local (Phase 3) and target-server-aware
  (Phase 5) execution paths accept; neither has a code path from a
  caller-supplied string to a process argument.
- **No arbitrary container names, host paths, or compose files** —
  unchanged: every call resolves its target exclusively from the already-
  validated `ApplicationEnvironment` row, including now its `TargetServer`
  navigation property (loaded via `.Include()`, never re-derived from
  request input).
- **No hardcoded server IPs, usernames, passwords, or SSH keys anywhere**
  — `NotConfiguredRemoteExecutionProvider` needs none of these (it never
  connects to anything); no such fields were added to `TargetServer` or
  configuration this phase.
- **The portal never assumes local Docker access represents a remote
  target server.** This is the core of the correction: the previous
  implementation's fundamental error was exactly this assumption, live-
  verified as fixed by configuring a `TargetServer` with a Hostname on a
  different subnet (`10.0.5.20`, no directory for it created anywhere in
  this sandbox) and confirming the status/restart/recreate endpoints all
  correctly report "unreachable" rather than silently succeeding against
  local (non-existent, irrelevant) paths.
- **Unauthorized environment access is impossible, not just hidden** —
  unchanged, still enforced before any provider is even consulted.
- **No secrets in audit logs** — unchanged; `LogSanitizer.Sanitize()`
  still runs on every provider-returned message before it's written to
  `AuditLog.Details`.

### Testing

**Automated**: 209 tests total, all passing (`dotnet test`, zero filter,
zero failures). Net change from the original Phase 5 total (199): the 5
tests for the deleted local-only `ContainerInspector` were replaced by
15 tests split across two new files exercising the new two-layer
abstraction, and `ContainerOperationsServiceTests` gained target-server-
routing and unreachable-path coverage:
- `NotConfiguredRemoteExecutionProviderTests` (3) — `IsConfigured` is
  always `false`; both methods always fail with a target-server-specific
  message and never throw.
- `DockerComposeContainerRuntimeProviderTests` (9) — the
  discovery/enrichment/parsing logic (both documented `compose ps` JSON
  shapes, malformed-output tolerance, `docker inspect` enrichment
  preferred over `compose ps` fallback fields, `Unhealthy` mapping) is
  fully exercised against a fake `IRemoteExecutionProvider`; separately,
  both `GetStatusAsync` and `RunOperationAsync` are proven to short-
  circuit to `IsReachable: false` **without invoking the fake at all**
  when `IsConfigured` reports `false` — the exact behavior that keeps a
  real future implementation from being bypassed.
- `ContainerOperationsServiceTests` (23) — every original Phase 5 test
  case (status retrieval, unauthorized control, authorized restart/start/
  stop, invalid target, unhealthy state, failed operation, audit events,
  recreate's full gate matrix) still passes, now routed through a fake
  `IContainerRuntimeProvider` and asserting the correct `TargetServer` is
  passed on every call; plus new cases proving the *default* (no fake
  override) fixture — which mirrors `NotConfiguredRemoteExecutionProvider`'s
  real behavior — reports unreachable rather than a false success, and
  that a mid-sequence unreachable result during recreate stops before
  attempting `up -d` (only `down -v` was attempted).

**Live/manual verification** (real Postgres 16, real `dotnet run` API,
real HTTP calls): configured a `TargetServer` named `remote-server-1`
with `Hostname: 10.0.5.20` (a different subnet, and — deliberately — no
corresponding directory created anywhere in this sandbox, to prove
nothing local is silently substituted):
- `GET .../containers` → `IsConfigured: true`, `IsReachable: false`,
  `TargetServerName: "remote-server-1"`, `UnreachableReason: "No remote
  execution mechanism is configured for target server 'remote-server-1'."`,
  empty `Containers` — HTTP 200, never an exception.
- `POST .../containers/restart` → HTTP 200,
  `{"success": false, "message": "No remote execution mechanism is
  configured for target server 'remote-server-1'."}`.
- `POST .../containers/recreate` correctly still enforces its
  configuration-boundary gates *before* ever consulting the (always-
  unreachable) provider: rejected with the `UseDownWithVolumesOnDeploy`
  validation error when that flag was off, regardless of `Confirm`; after
  enabling it, the call proceeds to the provider layer and reports
  `success: false` with the same unreachable message.
- `GET /api/audit` afterward shows, in order: `container.restart`
  (`Failure`), `container.recreate.requested` (`Success`, logged before
  any provider call), `container.recreate.failed` (`Failure`) — every
  action still accounted for.
- Regression: `dotnet ef migrations has-pending-model-changes` → none;
  Phase 1–4 login, roles, target-server/allowed-root creation, and
  application-environment configuration re-confirmed working unchanged in
  the same session.

Live-verification database was removed afterward; nothing from this
manual pass was left running or committed.

### Known limitations / explicit Phase 6+ candidates

- **No real remote execution mechanism exists.** This is the central,
  deliberate gap this correction leaves open rather than papering over.
  Building one requires, at minimum: a secure credential storage design
  for however target servers will be authenticated (SSH key/agent
  socket, mTLS client cert for a remote agent, etc. — env-var-name
  references only, matching the `Repository.AccessTokenEnvVarName`/
  `Smtp:Password` pattern already established, never raw secrets in the
  database); a decision between an SSH-based executor (simpler, needs
  host-key trust management) versus a lightweight per-target-server agent
  (more moving parts, better isolation — the agent could run with
  Docker-group access on its own host without ever exposing a socket to
  the portal); and updated `TargetServer` configuration to name which
  mechanism and credentials apply per server. None of this was built
  here — it's real, security-sensitive infrastructure work deserving its
  own deliberate phase and review, not an assumption baked into a
  monitoring feature.
- **No frontend for this phase**, unchanged from the original
  implementation — no Phase 4 UI page for container status/controls yet.
  Building one now would also need to honestly render "not yet reachable"
  as the default state for every configured environment, not just a
  loading spinner.
- **Phase 3's deployment executor still assumes local Docker access**,
  unchanged by this correction (see "Why Phase 3's deployment executor
  was deliberately NOT touched" above) — deployments still only work
  correctly when the portal process has local Docker access to the
  target. Unifying deployment execution onto `IRemoteExecutionProvider`
  once a real implementation exists would close this gap for both
  features at once, but is explicitly deferred, not assumed.
- **No historical container-status or health-check time series** —
  unchanged; still explicitly out of scope (monitoring/analytics
  territory).
- **`docker compose ps --format json`'s exact output shape is version-
  dependent and still unverified against a real Docker Compose
  installation** — unchanged limitation, now doubly true since there is
  also no real remote connection to verify it over yet. The parsing
  logic (`DockerComposeContainerRuntimeProvider`) is unit-tested against
  both documented shapes and degrades safely on anything else, but has
  never been exercised against real `compose ps` output end-to-end.
- **Container operations are LegacyFilesystem-mode only**, unchanged,
  matching Phase 3's `DeploymentExecutor` scope.

## Next phase

Not yet assigned — Phase 5 (Docker Container Monitoring & Operational
Controls), including its remote-execution architecture correction, is
complete; awaiting explicit approval before starting further work.
Strongest candidate per this phase's own "Known limitations": a real
secure remote-execution mechanism (SSH-based or per-target-server agent,
with proper credential storage) implementing `IRemoteExecutionProvider` —
until that exists, container monitoring/control remains abstraction-only
and Phase 3's deployment executor remains local-Docker-only. Other
candidates, unchanged from before: the Phase 4 UI work this phase's API
was shaped for (a container-status page, restart/stop/start controls,
live-refresh polling — now also needing to honestly render "not yet
reachable" as the default state), or hardening session storage to an
httpOnly cookie (flagged since Phase 4). Do not assume which without
asking.
