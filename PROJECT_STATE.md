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

## Next phase

Not yet assigned — Phase 3 (Deployment Engine & Environment Promotion
Workflow) is complete; awaiting explicit approval before starting further
work. Strongest candidate per the "Known limitations" above: a real
remote-execution story (credential vault + either SSH or a scoped
per-target-server agent) so deployments can actually reach servers other
than the one running the portal API process — the current executor is
correct and safe, but only for same-host Docker access. Do not assume
which without asking.
