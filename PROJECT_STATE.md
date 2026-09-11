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

**Phase 6 — Build Pipeline & Jenkins Integration.** Done. Adds a
provider-abstracted build pipeline (`IBuildProvider`, implemented for real
by `JenkinsBuildProvider` over the Jenkins REST API) so applications can go
Source → Jenkins build → Docker image → Registry → immutable `Release`,
while every existing Legacy (prebuilt publish directory) application keeps
working completely unchanged. Requesting a build never deploys anything.
Unlike Phase 5's remote-Docker gap, Jenkins connectivity is genuinely
operational here (plain HTTP from the portal, no socket/SSH problem) —
see dedicated section below for exactly what is and isn't wired up.

**Phase 7 — Secrets & Secure Configuration Management.** Done. Adds
`SecretReference` (metadata only) + `ISecretProvider` (real first
implementation: AES-256-GCM encryption at rest, `EncryptedSecretProvider`)
so database/API/registry/GitLab/Jenkins/SMTP/server credentials are never
stored in Git or plaintext in the database. Secrets are environment-aware
(a DEV-scoped secret is structurally unreachable when resolving QA/UAT/
Production) and resolved only at deployment execution time, injected into
`docker compose` as real process environment variables — never as
arguments, never persisted, never returned by any API response. See
dedicated section below.

**Phase 8 — Notifications & Approval Workflow.** Done. Adds
`INotificationProvider` (real first implementation: `EmailNotificationProvider`,
wrapping Phase 3's SMTP plumbing) and `INotificationService`, which notifies
the right users — resolved by permission, never role name — on every
QA/UAT/production approval request, deployment started/succeeded/failed,
and rollback completed/failed. Production/CTO approval keeps Phase 3's
exact state machine (never auto-deploys); its notification email now
deep-links to a read-only, unauthenticated preview via a secure, expiring,
SHA-256-hashed one-time token that is never itself a path to approve or
reject anything. See dedicated section below.

**Phase 9 — Productization & Multi-Tenant Architecture.** Done. Introduces
`Tenant` (organization/customer) as the top-level ownership boundary and
retrofits every tenant-owned entity with a `TenantId`, enforced
server-side via EF Core global query filters — no existing service's
query logic changed to get this, only a scalar `TenantId` column and one
`HasQueryFilter(...)` line per entity in `AppDbContext`. Adds tenant CRUD
(`TenantService`, platform-administrator-only), per-tenant role/environment
provisioning at tenant-creation time, organization-specific custom roles
(`RoleService.CreateAsync`/`UpdateAsync`), and Admin UI screens for
tenants/users/roles/repositories/deployment-targets/integrations. See
dedicated section below.

## Current database state

PostgreSQL via EF Core migrations (`src/DevOpsPortal.Infrastructure/Persistence/Migrations`):
`InitialCreate` (Phase 1), `AddLegacyDeploymentConfiguration` (Phase 2), ...,
`Phase6_BuildPipelineJenkins` (Phase 6 — see dedicated section below for its
`BuildServers`/`BuildRequests`/`Releases` tables and the additive columns on
`BuildConfigurations`/`Deployments`), `Phase7_SecretsManagement` (Phase 7 —
`SecretReferences` metadata table + `SecretValues` ciphertext-only table),
`Phase8_NotificationsApprovals` (Phase 8 — approval-token/notification
columns on `PromotionRequests`/`ProductionApprovals`; see dedicated section
below), `Phase9_MultiTenant` (Phase 9 — new `Tenants` table; `TenantId`
column + FK + index on every tenant-owned entity; composite tenant-scoped
unique indexes replacing several previously-global ones; see dedicated
section below).

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

## Phase 6 — Build Pipeline & Jenkins Integration

Done. Adds a provider-abstracted build pipeline so an application can move
from source code to a deployable, traceable artifact — `IBuildProvider`,
implemented for real by `JenkinsBuildProvider` over the Jenkins REST API —
while every existing Legacy (prebuilt publish directory) application keeps
deploying exactly as Phase 3 left it. **No database schema change to any
existing deployment behavior**: `BuildServers`/`BuildRequests`/`Releases`
are new tables, and the new columns on `BuildConfiguration`/`Deployment`
are all nullable and additive.

### Architecture

```
BuildService (Application)
        │  permission checks, validation, audit, DTO shaping,
        │  refresh-on-read status resolution (no background poller —
        │  same choice Phase 5 made for live container status)
        ▼
IBuildProvider (Application/Abstractions, impl: JenkinsBuildProvider)
        │  provider-agnostic contract: TriggerBuildAsync / GetBuildStatusAsync
        │  / GetBuildLogAsync — the deployment engine and BuildService never
        │  reference Jenkins directly; a future GitLab CI/GitHub Actions/
        │  TeamCity provider is another IBuildProvider registration, no
        │  caller change (master requirements §1)
        ▼
JenkinsBuildProvider (Infrastructure/Build) — REAL, operational HTTP client
        │  against the Jenkins REST API. Never throws for expected failures
        │  (network, auth, 404) — returns a Fail result, same idiom as
        │  IGitProviderClient/GitLabProviderClient (Phase 3).
        ▼
   Jenkins (buildWithParameters / build, queue/item/{id}, job/{name}/{n})
```

**Why this is unlike Phase 5's remote-execution gap**: Phase 5 could not
honestly implement remote Docker access because reaching an arbitrary
`TargetServer`'s Docker engine needs a secure connectivity mechanism (SSH
key/agent, credential vault, host-key trust) that doesn't exist yet. Jenkins
is different — it's a single, portal-configured HTTP(S) endpoint reachable
the same way GitLab already is (Phase 3's `IGitProviderClient`), so
`JenkinsBuildProvider` is a real, working implementation the moment a
`BuildServer` row points at a reachable Jenkins instance. There is no
placeholder/"NotConfigured" provider for builds — if none is registered for
a `BuildServer`'s `ProviderType`, that specific, honest failure is reported
per-request (see Security decisions below), not architecture-wide.

### Jenkins's real two-phase async lifecycle, modeled honestly

Triggering a Jenkins job (`POST .../buildWithParameters` or `.../build`)
returns **201 Created with a `Location` header pointing at a queue item**
(`.../queue/item/{id}/`) — never a build number. The build number is only
knowable once that queue item resolves to an `executable`
(`GET .../queue/item/{id}/api/json`). `IBuildProvider.TriggerBuildAsync`
therefore returns a queue-item reference, not a build number, and
`GetBuildStatusAsync` accepts either a known build number (cheap, precise —
`GET .../job/{name}/{n}/api/json`) or a queue-item id to resolve, following
up automatically with the build's own status the moment it resolves so a
caller sees `Running`/`Succeeded`/`Failed` on the same poll rather than
waiting for a second one.

### Refresh-on-read status, not a background poller

`BuildService.GetAsync` calls `IBuildProvider.GetBuildStatusAsync` and
persists the result only when a build is still `Queued`/`Running` —
matching Phase 5's "live status" design choice rather than adding new
background-worker infrastructure. `ListAsync` deliberately does **not**
refresh every row (would mean one external HTTP call per build in the
list); only `GetAsync` (by id) refreshes. A GitHub/CI webhook that pushes
status instead of this pull model is a natural future improvement, not
built here.

### Release: the immutable artifact record

A `Release` row is created exactly once, the moment a `BuildRequest`
resolves to `BuildStatus.Succeeded` with a known build number — one Release
per successful BuildRequest (`Release.BuildRequestId` is a unique FK), never
mutated afterward. A **failed** build produces no Release — the failure is
fully captured on the `BuildRequest` itself (`Status`/`ErrorMessage`), so
there's nothing "immutable" to record beyond that. Only the Modern
(Jenkins → Docker image) path in this phase ever produces a Release; Legacy
deployments have none and don't need one.

`Release.ImageReference` is always traceable and never `latest`:
`{registry}/{repository}:{tag}`, where `tag` is chosen from
`BuildConfiguration.ImageTagStrategy` — `CommitSha` uses the build's commit
(falling back to the build number if no commit was supplied for that
request — still a real, unique, non-"latest" identifier, not a hard
failure), `BuildNumber` uses the resolved provider build number, and
`SemVer` uses the caller-supplied `RequestBuildRequest.SemVer` (required at
request time for that strategy — there is no way to derive a semantic
version automatically, so this is validated eagerly rather than failing
silently after a build already ran).

### Deployment ↔ Release: schema-ready, not execution-wired

Per master requirements §6 ("Deployment should reference an immutable
release"), `Deployment` gained a nullable `ReleaseId` FK to `Release` this
phase — purely additive, `Restrict` on delete. **What this phase does not
do**: wire `IDeploymentService`/`DeploymentExecutor` to accept a Release and
deploy its image. That's deliberately out of scope here, for the same
reason Phase 3 left `BuildConfiguration` "config only" and Phase 5 left
`IRemoteExecutionProvider` abstraction-only: `DeploymentExecutor`'s
`DeploymentMode.ContainerImage` branch already, explicitly, throws
"Container-image deployment execution is not implemented yet" (Phase 3) —
actually wiring deploy-from-release would mean implementing that branch for
real, which is a deployment-engine change, not a build-pipeline one, and
risks exactly the "change the existing Phase 3 workflow unnecessarily"
outcome the instructions for this phase (and Phase 5's correction before
it) explicitly warn against. So today: every `Deployment.ReleaseId` is
`null` for every deployment created by any code path, `Release` rows exist
and are fully queryable/auditable, and a future phase can wire
`CreateDevDeploymentRequest`/`DeploymentService` to accept a `ReleaseId`
and populate `ImageReference`/`CommitSha`/`VersionLabel` from it without any
schema change.

### Legacy support — both models genuinely coexist

- **Legacy** (`BuildConfiguration.BuildServerId == null`): completely
  unchanged from Phase 3. `POST .../builds` rejects a build request for
  such an application with a clear `ValidationException` ("no
  build-from-source configuration... use the Legacy deployment path") —
  it does not silently no-op or fabricate a build. Every Legacy
  application's existing `DeployToDev`/promotion/rollback flow is
  byte-for-byte unchanged; `DeploymentExecutor`/`IComposeCommandExecutor`
  were not touched this phase.
- **Modern** (`BuildConfiguration.BuildServerId` + `JobName` set): the new
  `POST .../builds` → `GET /api/builds/{id}` → `Release` flow described
  above. Deploying the resulting image is not wired yet (see previous
  section) — this phase delivers Source → Build → Image → Registry →
  Release; Release → running Deployment is the next phase's work, same as
  Phase 3 left Mode B's execution for a later phase.

### Database changes

New tables (`Phase6_BuildPipelineJenkins` migration):
- **`BuildServers`** — `Name` (unique), `Description`, `ProviderType`,
  `BaseUrl`, `Username`, `ApiTokenEnvVarName` (env var *name* only — see
  Security below), `IsActive`, `CreatedAt`.
- **`BuildRequests`** — `ApplicationId`/`BuildServerId` (FK, `Restrict`),
  snapshotted `JobName`, `Branch`, `CommitSha`, `RequestedSemVer`, `Status`,
  `ProviderQueueItemId`, `BuildNumber`, `BuildUrl`, `ErrorMessage`,
  `RequestedByUserId`/`RequestedAt`, `StartedAt`/`CompletedAt`.
- **`Releases`** — `ApplicationId` (FK, `Restrict`), `BuildRequestId`
  (unique FK, `Cascade` — a Release cannot outlive the BuildRequest that
  produced it), `CommitSha`, `Branch`, `BuildNumber`, `ImageReference`,
  `BuildStatus`, `CreatedAt`.

Additive columns (nullable, zero impact on any existing row):
- `BuildConfigurations.BuildServerId` (FK, `Restrict`), `JobName`,
  `SdkVersion`, `PublishArguments`.
- `Deployments.ReleaseId` (FK, `Restrict` — see "schema-ready, not
  execution-wired" above).

Confirmed via `dotnet ef migrations has-pending-model-changes` (none after
the migration) and a live `dotnet ef database update` against a fresh
Postgres 16 instance (applies cleanly on top of every Phase 1–5 migration).

### Security decisions

- **Jenkins credentials are never stored in source code or the database.**
  `BuildServer.ApiTokenEnvVarName` is only the *name* of a server-side
  environment variable (validated as a plausible env-var name, e.g.
  `JENKINS_TOKEN_MAIN`); the actual token is read via
  `Environment.GetEnvironmentVariable(...)` at call time inside
  `JenkinsBuildProvider`, used only in the `Authorization: Basic` header,
  and never logged or returned by any API response — the exact pattern
  `Repository.AccessTokenEnvVarName` established in Phase 3.
- **No arbitrary Jenkins jobs from users.** `BuildTriggerRequest.JobName`
  always comes from the application's own `BuildConfiguration.JobName` —
  `RequestBuildRequest` (the API body) has no job-name field at all, only
  `Branch`/`CommitSha`/`SemVer`.
- **Server-side authorization enforced inside `BuildService`** (same
  `EnsurePermissionAsync` pattern as `DeploymentService`/
  `ContainerOperationsService`, not just a controller attribute) —
  `builds.request` to trigger, `builds.view` to read status/list/logs;
  `buildservers.view`/`buildservers.manage` gate the separate
  `BuildServersController`. Live-verified: a request with no auth token is
  rejected with HTTP 401 before reaching any service.
- **No secrets in build logs.** `IBuildService.GetLogAsync` runs
  `LogSanitizer.Sanitize()` on every line of console text proxied from
  Jenkins before returning it — live-verified against a build log
  containing a leaked `API_TOKEN=...` line, which came back as
  `API_TOKEN=***REDACTED***`.
- **Every build request is audited**, success or failure, via one
  `auditService.LogAsync("build.request", ...)` call naming the
  application, job, build server, branch/commit, and (on failure) the
  sanitized error — live-verified: both a successful trigger and an
  unreachable-server failure appear in `GET /api/audit`.
- **Build logs are proxied, never persisted.** Jenkins remains the durable
  log store; the portal only ever returns what Jenkins returns for that
  request, matching the "don't duplicate what's already durably stored
  elsewhere" principle used for container status in Phase 5.

### API surface

- `POST /api/applications/{id}/builds` (`builds.request`, enforced inside
  the service) — body `{ branch?, commitSha?, semVer? }`.
- `GET /api/applications/{id}/builds` (`builds.view`) — list for an app.
- `GET /api/builds?applicationId={id?}` (`builds.view`) — flat list,
  optionally filtered.
- `GET /api/builds/{id}` (`builds.view`) — refresh-on-read status.
- `GET /api/builds/{id}/logs` (`builds.view`) — sanitized console log
  proxy.
- `GET`/`POST`/`PUT /api/build-servers[/{id}]` (`buildservers.view`/
  `buildservers.manage`) — CRUD for configured build servers.
- `GET`/`PUT /api/applications/{id}/build-configuration` (unchanged
  routes, extended body: `buildServerId`, `jobName`, `sdkVersion`,
  `publishArguments`).

### Permission model (additions)

| Role | New `builds.*`/`buildservers.*` permissions |
|---|---|
| DEVELOPER | `builds.view`, `builds.request` |
| QA | `builds.view` |
| UAT | `builds.view` |
| DEVOPS | `buildservers.view`, `buildservers.manage`, `builds.view`, `builds.request` |
| CTO | `builds.view` |
| ADMIN | all (superset, as in every prior phase) |

### Testing

**Automated**: 249 tests total, all passing (`dotnet test`, zero filter,
zero failures) — net +40 over Phase 5's 209:
- `JenkinsBuildProviderTests` (16) — trigger with/without parameters
  (endpoint choice, query-string encoding), queue-item Location-header
  parsing, non-success status handling, missing-Location handling,
  build-number-based status (`Running`/`Succeeded`/`Failed` result
  mapping), queue-item-based status (still-queued, cancelled, resolves-
  and-follows-up), console log fetch + completeness detection, and
  never-throws-on-unreachable-host for every method — using a fake
  `HttpMessageHandler`, no real Jenkins required.
- `BuildServiceTests` (16) — authorization (missing `builds.request`,
  view-only user denied a request, missing `builds.view`), no
  build-configuration / unknown application / SemVer-without-SemVer
  validation, successful trigger persists `Queued` and never creates a
  `Deployment`, status refresh resolving to `Succeeded` creates a `Release`
  with the correct traceable `ImageReference` (including the commit-
  missing → build-number-tag fallback), failed trigger persists `Failed`
  with a sanitized error and a `Failure`-result audit entry, status refresh
  resolving to a build-level `Failed` creates no `Release`, an unregistered
  provider type produces an honest `Failed` `BuildRequest` rather than
  throwing, and log fetch both for an unresolved build number (unavailable)
  and sanitizing a leaked secret from provider log text.
- `BuildServerServiceTests` (6) — credential-reference-only round-trip,
  duplicate-name conflict, invalid `BaseUrl` (non-URL, non-http(s) scheme),
  malformed `ApiTokenEnvVarName`, not-found on update, and in-place update.
- `BuildConfigurationServiceTests` (+3 over Phase 3's existing 6) — unknown
  `BuildServerId` rejected, `BuildServerId` without `JobName` rejected, and
  a full modern-path upsert round-trip including the joined
  `BuildServerName`.

**Live/manual verification** (real Postgres 16, real `dotnet run` API, a
minimal fake Jenkins REST server — no real Jenkins install available in
this sandbox, same constraint Phase 3 had for a real GitLab instance):
- Created a `BuildServer` pointing at `http://127.0.0.1:1` (nothing
  listening) → `POST .../builds` returned HTTP 200 with
  `status: Failed, errorMessage: "Could not reach build server
  'unreachable-jenkins'."` — never a 500, never a fabricated success.
- Re-pointed the same `BuildServer` at a fake Jenkins implementing the real
  three endpoints (`POST .../build` → 201 + `Location: .../queue/item/1/`,
  `GET .../queue/item/1/api/json` → resolves to build `#5`,
  `GET .../job/.../5/api/json` → `result: SUCCESS`,
  `GET .../job/.../5/consoleText` → log text containing a fake leaked
  token): `POST .../builds` returned `status: Queued,
  providerQueueItemId: "1"`; the next `GET /api/builds/{id}` resolved it to
  `status: Succeeded, buildNumber: 5, releaseId: <guid>, imageReference:
  "registry.example.com/group/sample-app:abc123def456"`.
- `GET /api/builds/{id}/logs` returned the console text with
  `API_TOKEN=***REDACTED***` in place of the fake leaked secret.
- `GET /api/audit?action=build.request` showed both the failed and
  successful requests, with `result: Failure`/`Success` respectively and
  the sanitized detail message.
- `GET /api/deployments?applicationId=...` returned `[]` throughout —
  confirmed a successful build never created a Deployment.
- A request with no `Authorization` header was rejected with HTTP 401
  before reaching `BuildService`.
- Regression: `dotnet ef migrations has-pending-model-changes` → none;
  Phase 1–5 login, target-server listing, and application listing
  re-confirmed working unchanged in the same session.

Live-verification database and the fake Jenkins process were removed
afterward; nothing from this manual pass was left running or committed.

### Known limitations / explicit next-phase candidates

- **Deploy-from-release is schema-ready but not execution-wired** — the
  single biggest deliberate gap this phase leaves open. `Deployment.ReleaseId`
  exists and `Release` rows are created correctly, but nothing in
  `IDeploymentService`/`DeploymentExecutor` yet accepts a `ReleaseId` or
  deploys a `Release`'s image — that requires implementing
  `DeploymentExecutor`'s `DeploymentMode.ContainerImage` branch for real
  (today it explicitly throws "not implemented yet", unchanged since Phase
  3), which is deployment-engine work, not build-pipeline work, and was
  deliberately left alone per the instruction not to change Phase 3's
  workflow unnecessarily.
- **No frontend for this phase** — no Phase 4 UI page for requesting a
  build, viewing build status/logs, or browsing releases yet.
- **Refresh-on-read only, no webhook/push status** — `GET /api/builds/{id}`
  is the only thing that refreshes a build's status; nothing calls Jenkins
  proactively, so a build that is never polled by a caller after being
  triggered stays `Queued`/`Running` in the database indefinitely (harmless
  — just stale — but worth a webhook receiver in a future phase).
  `ListAsync` never refreshes, by design (see "Refresh-on-read" above).
- **Only Jenkins is implemented.** `IBuildProvider` is provider-agnostic by
  design (master requirements §1), but Jenkins is the only registered
  implementation — a second provider (GitHub Actions, GitLab CI, TeamCity)
  is a new `IBuildProvider` + DI registration, no change to `BuildService`
  or any caller.
- **SemVer tagging requires a caller-supplied version every time** — there
  is no automatic semantic-version derivation (e.g. from Git tags); this is
  an explicit, validated requirement at request time for that strategy, not
  a silent gap.
- **Jenkins folder-style job names are supported in `JenkinsBuildProvider`
  (`folder/job` → `.../job/folder/job/job`) but untested against a real
  folder-based Jenkins install** — same "unverified against a real
  installation" caveat Phase 5 documented for `docker compose ps`'s
  version-dependent output shape.
- **No Jenkins CSRF crumb caching** — `JenkinsBuildProvider` fetches a
  fresh crumb on every trigger call (tolerating a disabled/absent crumb
  issuer) rather than caching it; fine at this request volume, worth
  revisiting if Jenkins is triggered at high frequency.

## Phase 7 — Secrets & Secure Configuration Management

Done. Implements secure management of deployment credentials/configuration
without storing actual secrets in Git — per the master requirements'
7-section scope: secret references, a provider abstraction, authorized-only
management with values never exposed, execution-time-only resolution,
environment isolation, and a value-free audit trail.

### Architecture

```
SecretReferenceService (Application)
        │  permission checks, validation, audit, DTO shaping (metadata
        │  only — never a value); the only Application-layer code that
        │  talks to ISecretProvider
        ▼
ISecretProvider (Application/Abstractions, impl: EncryptedSecretProvider)
        │  provider-agnostic value store: StoreAsync/RetrieveAsync/DeleteAsync
        │  keyed by an opaque StoreKey — SecretReferenceService (and every
        │  other caller) never needs to know how or where a value is kept
        ▼
EncryptedSecretProvider (Infrastructure/Secrets) — REAL, operational
        │  AES-256-GCM encryption at rest, in a dedicated SecretValueRecord
        │  table only this class can reach (see "Ciphertext isolation" below)
        ▼
   Postgres (SecretValues: StoreKey, Ciphertext, Nonce, Tag — no plaintext,
   ever, anywhere in the database)
```

`SecretReference` (Domain) rows carry only metadata — `Name`, `Category`,
`Scope`, `ApplicationId`/`EnvironmentDefinitionId`, `ProviderKey` (which
backend), `StoreKey` (opaque, provider-internal, never exposed via the API
either). The actual value lives exclusively behind `ISecretProvider`.

### Provider abstraction, with a real first implementation

Unlike Phase 5's remote-Docker gap, encryption-at-rest needs no external
infrastructure to do for real — `EncryptedSecretProvider` is the master
requirements' "securely protected server-side secret store appropriate for
the product deployment," genuinely operational today, not a placeholder.
`ISecretProvider` is still deliberately provider-agnostic (`ProviderKey` on
every `SecretReference` names which backend holds it) so a future
HashiCorp Vault, cloud secret manager (AWS Secrets Manager/Azure Key
Vault/GCP Secret Manager), or Kubernetes Secrets implementation is a new
class + one DI registration — no change to `SecretReferenceService`,
`DeploymentExecutor`, or any controller.

### Ciphertext isolation — an architectural guarantee, not just a convention

`SecretValueRecord` (the ciphertext table) is **not** on `IAppDbContext` —
only the concrete `AppDbContext` carries that `DbSet`. `EncryptedSecretProvider`
is constructed with the concrete `AppDbContext`, not the interface every
other Application-layer service uses; `SecretReferenceService` and every
other service are constructed with `IAppDbContext` and have literally no
compiled code path to the ciphertext table, regardless of what they're
injected with or how they're called. Encryption is AES-256-GCM with a
fresh random 96-bit nonce per write (verified: encrypting the same
plaintext twice produces different ciphertext/nonce pairs) and a 128-bit
authentication tag — tampered or wrong-key ciphertext fails to decrypt
rather than silently returning garbage.

### Never store the key in source control

`Secrets:EncryptionKey` (env `SECRET_ENCRYPTION_KEY`, mapped in
`docker-compose.yml` as `Secrets__EncryptionKey`) must be ≥32 bytes,
validated at API startup exactly like `Jwt:SigningKey` — the process
refuses to start otherwise. `appsettings.json` ships with an empty string,
same as every other secret-shaped setting in this codebase (`Jwt:SigningKey`,
`Smtp:Password`). **Known limitation**: there is no key-rotation tooling —
changing this key after secrets exist makes every existing
`SecretValueRecord` permanently undecryptable (flagged in `.env.example`
and below).

### Environment isolation — structural, not conventional

`SecretScope` (`Global` / `Application` / `ApplicationEnvironment`) is
enforced both at creation (`SecretReferenceService.CreateAsync` rejects an
inconsistent Scope/ApplicationId/EnvironmentDefinitionId combination) and
at resolution. `ResolveForDeploymentAsync(applicationId,
environmentDefinitionId, ...)` only ever queries rows matching that exact
`(ApplicationId, EnvironmentDefinitionId)` pair at `ApplicationEnvironment`
scope, that `ApplicationId` at `Application` scope, or `Global` — a
DEV-scoped secret is never among the candidates when resolving QA, not
because of a runtime check that could have a bug, but because the query
itself cannot select it. Live-verified: a `db-password` secret scoped to
DEV only, resolved for DEV, returns its value; resolved for QA on the same
application, is simply absent — not empty-string, not a fallback, absent.
On a name collision across scopes, the most specific one wins per
environment (`ApplicationEnvironment` > `Application` > `Global`) — unit-
tested with a DEV-specific override existing alongside an app-wide default:
DEV resolves the override, QA falls back to the app-wide value, and the
DEV-only value is asserted absent from QA's resolved set.

### Resolved only at deployment execution time

`DeploymentExecutor.ExecuteLegacyFilesystemAsync` calls
`SecretReferenceService.ResolveForDeploymentAsync` immediately before
running `docker compose down`/`up`, passing the resolved
name→value dictionary through a new, purely additive
`ComposeCommandRequest.EnvironmentVariables` field. `ComposeCommandExecutor`
sets these as real `ProcessStartInfo.Environment` entries — never as
command-line arguments — so a compose file's `${VAR}` interpolation can see
them without the value ever appearing in `ps` output. Nothing is persisted:
the resolved dictionary exists only for the duration of one
`ExecuteAsync` call.

### Secrets never appear in logs, audit, exceptions, or API responses

- **Deployment logs**: a new `RedactSecretValues` step in `DeploymentExecutor`
  replaces every literal occurrence of a resolved secret's actual value in
  captured `stdout`/`stderr` with `***REDACTED***` *before* `LogSanitizer.Sanitize`'s
  existing pattern-based redaction runs — belt-and-braces, since
  `LogSanitizer` only catches `KEY=VALUE`-or-Bearer-token-shaped text and a
  leaked value could appear in any shape. Live/unit-verified: a fake compose
  stderr containing `"connecting with password hunter2 to db host"` (no
  `KEY=VALUE` shape at all) comes back redacted.
- **Audit**: `secret.created`/`secret.updated`/`secret.deleted`/
  `secret.referenced` are all audited (master requirements §6's exact list)
  with a details string built only from `Name`/`Category`/`Scope`/
  `ApplicationId`/`EnvironmentDefinitionId` — never the value. `secret.referenced`
  fires once per secret actually resolved during a deployment, attributed
  (via explicit `actorUserId`/`actorUsername` parameters, not
  `ICurrentUserService`) to the human who originally requested the
  deployment, since `DeploymentExecutor` runs in the background worker with
  no HTTP-request-scoped current user.
- **Exceptions**: `ResolveForDeploymentAsync` throws
  `DeploymentExecutionException($"Failed to resolve secret '{name}'...")`
  on any provider failure — the secret's *name*, never a value or the
  provider's raw error detail, which could conceivably describe the
  ciphertext.
- **API responses**: `SecretReferenceDto` has no `Value` property and no
  `StoreKey` property — structurally, not by convention (a dedicated test
  asserts this via reflection over the DTO's properties). There is no
  endpoint, anywhere in this codebase, that returns a secret's plaintext
  value — `ResolveForDeploymentAsync` is the only method that ever returns
  one, and it is only ever called by `DeploymentExecutor`, never wired to
  any controller.

### Legacy support / no breaking changes

`ComposeCommandRequest.EnvironmentVariables` is a trailing optional
parameter defaulting to `null` — every existing call site (Phase 3's other
compose invocations, Phase 5's `IRemoteExecutionProvider` path, which reuses
the same record) compiles and behaves exactly as before. An application
with no configured secrets resolves an empty dictionary and deploys exactly
as it did before this phase — Phase 3's deployment workflow is otherwise
untouched, per the same "don't change it unnecessarily" principle every
phase since Phase 5's correction has followed.

### Database changes

New tables (`Phase7_SecretsManagement` migration):
- **`SecretReferences`** — `Name`, `Category`, `Scope`,
  `ApplicationId`/`EnvironmentDefinitionId` (nullable FKs — `Application`
  cascades since these are configuration, matching `BuildConfiguration`/
  `ApplicationEnvironment`; `EnvironmentDefinition` restricts, matching
  every other FK to that shared reference table), `Description`,
  `ProviderKey`, `StoreKey`, `IsActive`, `CreatedByUserId`, `CreatedAt`,
  `UpdatedAt`. No DB-level unique index on
  `(Name, Scope, ApplicationId, EnvironmentDefinitionId)` — SQL's NULL ≠
  NULL comparison semantics don't give correct uniqueness across the
  nullable columns, so `SecretReferenceService.CreateAsync` enforces it
  explicitly instead (an `AnyAsync` check, same pattern as `TargetServer.Name`).
- **`SecretValues`** — `StoreKey` (PK), `Ciphertext`/`Nonce`/`Tag`
  (`bytea`), `CreatedAt`, `UpdatedAt`. Not on `IAppDbContext` — see
  "Ciphertext isolation" above.

Confirmed via `dotnet ef migrations has-pending-model-changes` (none) and a
live `dotnet ef database update` against a fresh Postgres 16 instance
(applies cleanly on top of every Phase 1–6 migration).

### API surface

- `GET /api/secrets?applicationId=&environmentDefinitionId=&category=`
  (`secrets.view`) — metadata list.
- `GET /api/secrets/{id}` (`secrets.view`).
- `POST /api/secrets` (`secrets.manage`) — body includes the plaintext
  `Value`; the response never echoes it.
- `PUT /api/secrets/{id}` (`secrets.manage`) — `Value` optional: omit to
  change only `Description`/`IsActive`; supply to rotate in place (same
  reference, same scope, new ciphertext). `Name`/`Category`/`Scope`/
  `ApplicationId`/`EnvironmentDefinitionId` are immutable after creation —
  changing a secret's scope after the fact is exactly the kind of
  silent-widening-of-access environment isolation exists to prevent.
- `DELETE /api/secrets/{id}` (`secrets.manage`).
- No endpoint anywhere returns a secret value — there is no
  `GET .../value`, by design.

Permission checks are enforced inside `SecretReferenceService`, not a
static `[RequirePermission]` attribute — same pattern as containers/builds
— so authorization is exercised the same way whether or not a request
reaches the controller, and is directly unit-testable.

### Permission model

| Role | `secrets.*` permissions granted |
|---|---|
| DEVELOPER | none |
| QA | none |
| UAT | none |
| DEVOPS | `secrets.view`, `secrets.manage` |
| CTO | none |
| ADMIN | both (superset, as in every prior phase) |

**Deliberate choice**: only DEVOPS (plus ADMIN's superset) gets any
`secrets.*` permission — master requirements §3 explicitly authorizes
"DevOps users" to manage secret references and explicitly excludes
developers/QA/UAT from seeing values; rather than invent a middle ground
(metadata-visible-but-not-manageable for other roles) not asked for by the
spec, the safest reading was taken: no role outside DEVOPS/ADMIN can see
even secret *metadata* (which application/environment has which category
of secret configured) today. Loosening this to grant `secrets.view` more
broadly is a one-line change in `DataSeeder` whenever that's explicitly
wanted.

### Testing

**Automated**: 289 tests total, all passing (`dotnet test`, zero filter,
zero failures) — net +37 over Phase 6's 252:
- `EncryptedSecretProviderTests` (9) — round-trip store/retrieve, the
  plaintext never appears anywhere in the persisted `Ciphertext` bytes
  (checked via Latin1 byte-for-byte scan, not just a UTF-8 string check),
  in-place rotation reuses the same `StoreKey`, unknown-key and wrong-
  encryption-key lookups both fail cleanly rather than throwing, delete is
  idempotent, and encrypting the same plaintext twice produces different
  nonce/ciphertext pairs (nonce reuse would be a real AES-GCM
  vulnerability).
- `SecretReferenceServiceTests` (25) — authorization (missing
  `secrets.manage`, view-only user blocked from create/update/delete),
  API response safety (`SecretReferenceDto` has no `Value`/`StoreKey`
  property via reflection; a created DTO's JSON serialization never
  contains the plaintext supplied to create it), scope/Id consistency
  validation (all six invalid combinations), duplicate-name-in-scope
  conflict vs. same-name-different-scope allowed, rotation changing the
  resolved value vs. metadata-only update leaving it unchanged, delete
  removing both metadata and value, and the full environment-isolation
  matrix: a DEV-scoped secret never resolves for QA, an Application-scoped
  secret resolves for every environment of that app, a Global secret
  resolves everywhere, most-specific-scope-wins on a name collision
  without leaking the more specific value to a less specific lookup, an
  inactive secret is never resolved, one application's secret never
  resolves for another application, every create/update/delete/reference
  audit entry is checked to not contain the plaintext value (with
  `secret.referenced` additionally checked for correct actor attribution),
  and a provider retrieval failure surfaces as `DeploymentExecutionException`
  naming the secret, never the value.
- `DeploymentExecutorTests` (+3 over Phase 6-era's existing 6) — resolved
  secrets are passed as `ComposeCommandRequest.EnvironmentVariables` (never
  as arguments) and never appear in a persisted log line while their
  *names* do; a leaked value in compose output with no `KEY=VALUE` shape
  is still redacted; a secret-resolution failure marks the deployment
  Failed and compose is never invoked at all.

**Live/manual verification** (real Postgres 16, real `dotnet run` API):
- Created a Global-scoped SMTP secret and an `ApplicationEnvironment`-scoped
  DEV `db-password` secret via the real API — both responses confirmed to
  have no value/store-key field.
- Created a `DEVELOPER`-role user (no `secrets.*` grant by default) and
  confirmed `GET /api/secrets` → 403 `"Missing required permission
  'secrets.view'"` and `POST /api/secrets` → 403 `"Missing required
  permission 'secrets.manage'"`.
- Rotated the DEV secret's value via `PUT /api/secrets/{id}`, then pulled
  `GET /api/audit` and grepped the entire response for both the original
  and rotated plaintext values and the SMTP secret's value — zero matches;
  `secret.created`/`secret.updated` entries present with descriptive,
  value-free details.
- Unauthenticated `GET /api/secrets` → 401.
- Regression: `dotnet ef migrations has-pending-model-changes` → none;
  applications/target-servers/build-servers listings re-confirmed working
  unchanged in the same session.
- No real Docker daemon is available in this sandbox (same constraint
  noted in Phase 3/5's testing sections), so the actual `docker compose`
  process-environment-variable injection was verified via
  `DeploymentExecutorTests`' fake `IComposeCommandExecutor` (which records
  every `ComposeCommandRequest` it receives) rather than a live compose run.

Live-verification database was removed afterward; nothing from this manual
pass was left running or committed.

### Known limitations / explicit next-phase candidates

- **No key-rotation tooling for `Secrets:EncryptionKey` itself.** Rotating
  the master encryption key requires decrypting every `SecretValueRecord`
  with the old key and re-encrypting with the new one; no such migration
  tool exists yet. Changing the configured key without one makes every
  existing secret permanently undecryptable — `RetrieveAsync` would return
  `Fail` for all of them, not silently corrupt data, but that's still an
  outage waiting to happen if not documented, which is why it's called out
  here and in `.env.example`.
- **No frontend for this phase** — no Phase 4 UI page for managing secret
  references yet (master requirements §3's "Authorized DevOps users can
  manage secret references" is implemented as a real, permission-gated
  API; the UI to drive it is not built, consistent with Phases 5 and 6's
  own backend-only precedent).
- **Deploy-from-release (Phase 6's own top known limitation) still doesn't
  consume secrets** — `DeploymentExecutor`'s `ContainerImage` branch is
  still unimplemented, so secret resolution this phase only wires into the
  Legacy (`docker compose down`/`up`) path. Whichever future phase
  implements Modern-path deployment execution should resolve secrets the
  same way.
- **No orphaned-ciphertext cleanup on Application deletion.**
  `SecretReference.ApplicationId` cascades on delete, but `SecretValueRecord`
  has no FK relationship to `SecretReference` at all (by design — see
  "Ciphertext isolation"), so a deleted application's secret values would
  become unreferenced rows rather than being cleaned up automatically.
  Low real-world impact: no `DELETE /api/applications/{id}` endpoint exists
  anywhere in this codebase today, so applications are never actually
  deleted (only deactivated) — flagged here so it isn't forgotten if hard
  delete is ever added.
- **`ISecretProvider` has exactly one implementation.** Provider-agnostic
  by design (master requirements §2), but Vault/cloud-secret-manager/
  Kubernetes-Secrets support is a future phase's new class + DI
  registration, not started here.
- **Secret categories (Database/Api/Registry/GitLab/Jenkins/Smtp/Server/
  Other) are informational only**, not structurally bound to `Repository`/
  `BuildServer`/`TargetServer` rows — e.g. a `GitLab`-category secret isn't
  wired to a specific `Repository.AccessTokenEnvVarName`-style consumption
  path yet. `Repository`/`BuildServer` still use their own pre-existing
  env-var-name-reference fields, untouched by this phase; unifying them
  onto `SecretReference` is a natural future consolidation, not assumed
  here.

## Phase 8 — Notifications & Approval Workflow

Done. Makes deployment approvals and failures visible through
notifications, per the master requirements' 6-section scope: an email
notification abstraction, notification coverage for every listed workflow
event, the Production/CTO flow (unchanged state machine, richer
notification), secure approval links/tokens, and UI visibility of pending
approvals/status/requester/approver/timestamps/deployment status.

### Architecture

```
DeploymentService / DeploymentExecutor
        │  fires one of four notification events at the right point in
        │  the existing state machine — never a new state, never a new
        │  auto-deploy trigger
        ▼
INotificationService (Application, impl: NotificationService)
        │  resolves recipients by permission (never role name — same
        │  principle Phase 3 established for the original CTO email),
        │  composes the message, mints/verifies approval-preview tokens,
        │  audits every send, broadcasts to every registered provider
        ▼
INotificationProvider (Application/Abstractions, impl:
        │  EmailNotificationProvider)
        │  channel-agnostic send — the only implementation today wraps
        │  Phase 3's IEmailSender/SmtpEmailSender (SMTP config, credential
        │  handling, and graceful-no-op-when-unconfigured all untouched)
        ▼
   SMTP (or, later, Slack/Teams/webhook via a new provider + one DI line)
```

`IEmailSender`/`SmtpEmailSender` are deliberately unchanged — email
delivery mechanics were already right in Phase 3 (configurable SMTP, no
hardcoded credentials, returns `false` rather than throwing when
unconfigured or on send failure). This phase's job was the layer above it:
deciding *when* to notify, *who* to notify, and *what* to say — that's
`NotificationService`, and it's the only Application-layer code that talks
to `INotificationProvider`, same ownership pattern Phase 7 used for
`ISecretProvider`.

### Notification coverage (master requirements §2)

| Event | Trigger point | Recipients |
|---|---|---|
| QA approval requested | `RequestPromotionAsync`, target QA | holders of `deployments.approve.qa` |
| UAT approval requested | `RequestPromotionAsync`, target UAT | holders of `deployments.approve.uat` |
| Production approval requested | `RequestPromotionAsync`, target Production | holders of `deployments.approve.production` |
| CTO approval requested | *(same event as above — see below)* | *(same)* |
| Deployment started | `DeploymentExecutor.ExecuteAsync`, on `Status = Running` | the user who requested the deployment |
| Deployment succeeded | `DeploymentExecutor.ExecuteAsync`, success path | requester |
| Deployment failed | `DeploymentExecutor.ExecuteAsync`, catch block | requester (message includes the sanitized failure reason) |
| Rollback completed/failed | same two hooks, `Deployment.IsRollback` picks the wording | requester |

**"Production approval requested" and "CTO approval requested" are one
event in this system, not two** — `deployments.approve.production` is the
single permission that gates deciding a production `PromotionRequest`
*and* its linked `ProductionApproval` (Phase 3's `ApprovePromotionAsync`
already transitions both in one call), so the recipients are always
identical. Sending two separately-worded emails to the same people about
the same request would just be noise; one CTO-flavored notification
(`NotifyProductionApprovalRequestedAsync`) covers both master-requirements
bullets. This is documented here so a future reader doesn't "fix" it into
two emails.

Deployment-lifecycle notifications go to the requester only — not a
broader distribution list — a deliberate scope decision to avoid
notification fatigue on every DEV deploy; see Known limitations for the
natural extension (CC approvers on production outcomes) this leaves open,
not built here since master requirements §2 lists recipients only as "the
right users," which the requester unambiguously is.

### Approval security — read-only preview tokens, decisions stay authenticated

Every `PromotionRequest` (all three target environments) and every
`ProductionApproval` gets its own one-time token, minted by
`ApprovalTokenHelper.Generate()` (256-bit random, hex) the moment the
record is created. Only the SHA-256 hash is persisted
(`ApprovalTokenHash`) — the raw value exists only long enough to go into
the notification body and is never logged or stored. This satisfies every
bullet in master requirements §4:

- **Secure** — 256 bits of entropy; only a hash is ever at rest; a
  database read alone can never reproduce a working link.
- **Expire** — `ApprovalTokenExpiresAt`/`ProductionApproval.ExpiresAt`
  (default 7 days from creation); `GET .../by-token/{token}` reports
  `isExpired: true` past that point rather than pretending the link is
  still fresh.
- **Single-use where appropriate** — the token is read-only (see below),
  so "single-use" applies to the *decision* it points at, not the read
  itself: once a promotion/approval is decided, the same token keeps
  resolving but now shows the decided state, never a stale "still
  pending" view.
- **Not expose secrets** — the preview DTO (`ApprovalPreviewDto`) carries
  only application/environment/commit/requester/timestamps/status; there
  is no field on it, anywhere, that could be a secret.
- **Auditable** — every notification send is audited
  (`notification.promotion_approval_requested`/
  `notification.production_approval_requested`/
  `notification.deployment_started`/`notification.deployment_outcome`),
  and the existing `promotion.*`/`production_approval.*` audit trail
  (unchanged from Phase 3) still covers every decision.
- **Associated with the correct application/release/environment** —
  structural, not a convention: the token is looked up by its hash
  directly against exactly one `PromotionRequest` or `ProductionApproval`
  row (the same `WHERE ApprovalTokenHash = X` pattern as an API-key
  lookup), so a token minted for one promotion can never resolve, preview,
  or be confused with another — live- and unit-verified with two
  successive promotions of the same application (a rejected one and its
  replacement): the first promotion's token still resolves only to the
  first promotion's own data after the second is created.

**Deliberate design choice, explicitly reversing nothing from Phase 3**:
the token is read-only. `GET /api/promotions/by-token/{token}` and
`GET /api/promotions/production-approvals/by-token/{token}` are the only
two `[AllowAnonymous]` actions in the entire API — they return a preview
DTO with no way to approve, reject, or deploy anything. Actually deciding
a promotion still always goes through the same authenticated,
permission-checked `POST /api/promotions/{id}/approve`/`reject` endpoints
Phase 3 built — this was Phase 3's own explicit, reasoned decision
("there is no separate public 'click this link to approve' endpoint — by
design... the token is not, by itself, sufficient to approve anything")
and master requirements §4's security checklist is fully satisfiable
without reversing it: a magic-link-that-decides-things model was
considered and rejected here specifically because it would add a new
unauthenticated write surface onto the single most sensitive action in
the system (production deployment approval) for a convenience gain
(skipping login from the email link) that isn't asked for anywhere in the
spec — the flow diagram in master requirements §3 reads just as correctly
as "get the email, then log in to the portal to act," which is exactly
what this implementation does. The read-only preview still gives the
email real, immediate value (the recipient sees what they're being asked
to review before logging in) without that trade-off.

### Never auto-deploy (master requirements §3) — unchanged from Phase 3

The Production flow's exact sequence — UAT success → promotion request →
CTO approval email → CTO approves (via the authenticated endpoint) →
portal shows Approved → a separately-authorized user explicitly deploys —
is byte-for-byte the same state machine Phase 3 built.
`DeployApprovedPromotionAsync` still re-validates
`PromotionRequest.Status == Approved` **and**
`ProductionApproval.Status == Approved` server-side on every call; nothing
in this phase adds a code path from "notified" or "approved" to a
`Deployment` row. This phase only changes what happens around that
unchanged core: who gets told, and how securely they can preview what
they're being asked to decide.

### Database changes

`Phase8_NotificationsApprovals` migration:
- **`PromotionRequests`** gained `ApprovalTokenHash` (unique, indexed,
  required), `ApprovalTokenExpiresAt` (required), `NotifiedAt` (nullable),
  `NotificationRecipients` (nullable, comma-joined, audit/display only).
- **`ProductionApprovals`**: `ApprovalToken` → `ApprovalTokenHash`
  (renamed — Phase 3's field was already an unguessable reference; this
  phase adds hashing so even a database read can't reconstruct a working
  link), `EmailSentAt` → `NotifiedAt`, `EmailRecipients` →
  `NotificationRecipients` (both renamed for provider-agnostic
  terminology now that email is one of potentially several notification
  channels), plus a new `ExpiresAt` (required).

No `SecretValueRecord`/ciphertext table, no `IAppDbContext` change beyond
what the renamed/added columns need — this phase touches only the
promotion/approval tables.

Confirmed via `dotnet ef migrations has-pending-model-changes` (none) and
a live `dotnet ef database update` against a fresh Postgres 16 instance
(applies cleanly on top of every Phase 1–7 migration).

### API surface

- `GET /api/promotions/by-token/{token}` — **unauthenticated**, read-only
  preview of a `PromotionRequest` (QA/UAT/Production, whichever the token
  was minted for).
- `GET /api/promotions/production-approvals/by-token/{token}` — same
  contract, for the CTO-specific `ProductionApproval`.
- Every other Phase 3 promotion/deployment endpoint is unchanged — same
  routes, same request/response shapes plus the new `PromotionRequestDto`
  fields below.

`PromotionRequestDto` gained (for the Phase 4 UI's "pending approvals,
approval status, requester, approver, timestamps, deployment status" —
master requirements §5): `decidedByUsername`, `notifiedAt`,
`ctoDecidedByUserId`/`ctoDecidedByUsername`/`ctoDecidedAt` (previously
only the status was exposed, not who decided or when),
`ctoNotifiedAt` (renamed from `ctoEmailSentAt`), and
`linkedDeploymentId`/`linkedDeploymentStatus` (the most recent `Deployment`
row created from this promotion, if any — resolves "deployment status"
without a separate call).

### Frontend

Unlike Phases 5–7 (backend-only by explicit precedent), this phase's own
section 5 asks for concrete UI fields, and the Phase 4 `PromotionCard`
component already existed and already covered most of them — so this
phase made the small, targeted addition the new DTO fields unlock rather
than leaving another explicit gap: an "Approver" row next to "Decided at",
a "Deployment status" badge when a `Deployment` has resulted from the
promotion, and the CTO-approval block now shows who granted/rejected it
and when. No new page, no new route — same component, same data flow,
just no longer silently dropping fields the backend now provides.

### Testing

**Automated**: 314 backend tests total, all passing (`dotnet test`, zero
filter, zero failures) — net +25 over Phase 7's 289; frontend: 38 tests,
all passing (+3 for the new `PromotionCard` fields):
- `NotificationServiceTests` (7) — no-recipients case sends nothing and
  never throws; broadcasts to every registered provider even when one
  fails or throws (provider isolation); deployment-started/outcome
  notifications resolve the requester's email and pick rollback vs.
  deployment wording correctly; a failed deployment's notification
  includes the failure reason; every send is audited.
- `ApprovalTokenHelperTests` (5) — 256-bit hex tokens, never repeated,
  deterministic hashing, `Verify` correct for right/wrong token.
- New `DeploymentServiceTests` coverage (13) — QA/UAT approval-requested
  notifications actually fire (previously only Production/CTO was
  tested) and are audited; a valid token previews correctly with no
  secret field; an unknown token 404s; a decided promotion's preview
  reflects the decision; an expired token still resolves but reports
  `isExpired: true`; **the full "wrong release" isolation matrix** — a
  token minted for one promotion (or one `ProductionApproval`) never
  resolves, previews, or leaks into another, even for the same
  application, verified across a rejected-then-replaced promotion pair
  for both QA/UAT and Production tokens; unauthenticated CTO-approval
  preview.
- `PromotionCard.test.tsx` (+3) — approver name/timestamp render once a
  promotion is decided, deployment status badge renders once a
  `Deployment` is linked, CTO approver name/timestamp render once
  granted.

Pre-existing Phase 3 coverage (`ApprovePromotionAsync_AlreadyDecided_ThrowsConflict`,
the full RBAC/state-machine/concurrency suite) already covered "duplicate
approval," "production authorization," and "audit" from master
requirements §6's checklist and needed no changes — confirmed still
passing unmodified.

**Live/manual verification** (real Postgres 16, real `dotnet run` API, a
minimal local SMTP debug server — no real relay available in this
sandbox, same constraint every phase with an external-service dependency
has had): drove the full DEV → QA → UAT → Production pipeline through the
real API and confirmed, for real, over real SMTP:
- `Deployment started`/`Deployment failed` emails delivered to the
  requester with the correct application/environment/commit/failure
  reason (no real Docker daemon in this sandbox, so DEV/QA/UAT deploys
  genuinely failed at the `docker compose` step exactly like Phase 3's own
  live verification — deployment rows were advanced to `Succeeded`
  directly in the database only to unblock the next promotion step, never
  by weakening the health-check/success logic itself).
- `Approval requested: ... -> QA`/`-> UAT` emails delivered to the correct
  permission-holders; `GET /api/promotions/by-token/{token}` with the
  token decoded straight out of the raw SMTP payload returned the correct
  preview with **no Authorization header** — genuinely unauthenticated;
  an unrelated/garbage token → 404.
- Duplicate approval of the same QA promotion → HTTP 409 on the second
  call.
- `Production approval requested: ...` email delivered only to
  `deployments.approve.production` holders; its token's preview showed
  `toEnvironmentName: "PRODUCTION"`; `POST .../deploy` before CTO approval
  → HTTP 400; after approval (`ctoDecidedByUsername: "admin"` correctly
  populated) → deploy succeeded.
- `GET /api/audit` showed a `notification.*` entry for every send above
  plus the unchanged `promotion.*`/`production_approval.*` entries; the
  entire audit response was grepped for both raw tokens used in this
  session — zero matches.
- Regression: `dotnet ef migrations has-pending-model-changes` → none;
  applications/target-servers/build-servers/secrets listings and
  unauthenticated-request-rejection (401) all re-confirmed working
  unchanged in the same session.

Live-verification database, the local SMTP debug server, and the
temporary `/tmp` compose fixture directory were all removed afterward;
nothing from this manual pass was left running or committed.

### Known limitations / explicit next-phase candidates

- **Deployment-lifecycle notifications go to the requester only** — no CC
  list for approvers/DevOps on a production failure, no team/channel
  distribution. A reasonable, low-risk future addition (e.g. also
  notifying `deployments.approve.production` holders on a *production*
  deployment failure specifically) — not built here since master
  requirements §2 doesn't specify a broader audience and doing so
  unprompted risks notification fatigue.
- **No "approval decided" notification back to the requester** — master
  requirements §2's list is entirely about *requested* events (approvals
  requested, deployment started/succeeded/failed, rollback
  completed/failed); whether a promotion was approved or rejected is
  visible in the portal (`PromotionRequestDto.status`/`decidedByUsername`)
  but doesn't currently trigger its own email. Easy, explicitly-scoped
  future addition if wanted.
- **`INotificationProvider` has exactly one implementation.** Provider-
  agnostic by design (master requirements §1's "notification
  abstraction"), but Slack/Teams/webhook support is a future phase's new
  class + one DI registration, not started here — same pattern Phase 6
  left `IBuildProvider` in and Phase 7 left `ISecretProvider` in.
  Preserved list of things "explicitly out of scope" from Phase 3's own
  notes (Slack/Teams integrations) now has exactly the extension point it
  was waiting for.
- **No key/token rotation tooling beyond natural expiry** — an approval
  token cannot be manually invalidated before its 7-day expiry (e.g. "I
  sent that to the wrong CTO"); the only mitigation today is that the
  token is read-only, so the worst case is someone previewing metadata
  they weren't the intended recipient of, not an unauthorized decision.
- **No frontend for build/release/secret-reference management still** —
  unchanged from Phases 6/7; this phase's frontend touch was scoped
  narrowly to the `PromotionCard` fields master requirements §5
  explicitly asked for, not a general UI expansion.

## Phase 9 — Productization & Multi-Tenant Architecture

Done. Objective: make the platform usable by multiple organizations
without redesigning the deployment engine. Every design choice below was
made to keep that engine (DeploymentExecutor's actual compose/health-check/
secret-resolution/notification logic) byte-for-byte unchanged — only
mechanical tenant-context threading was added to it.

### Architecture

```
Tenant (new root entity — Id, Name, Slug (globally unique), Description, IsActive)
   │  owned by: applications, repositories, environments, deployment
   │  targets, deployments (+ logs, promotions, production approvals),
   │  build servers/requests/releases, secret references, audit logs,
   │  and (nullable) users/roles
   ▼
Every tenant-owned entity carries a `TenantId` column (non-nullable Guid;
nullable Guid? on User/Role/AuditLog specifically — see below)
   ▼
AppDbContext.OnModelCreating: one HasQueryFilter(x => x.TenantId ==
currentTenantService.TenantId) per tenant-owned entity type
   ▼
ICurrentTenantService (Application.Abstractions) — read-only `Guid?
TenantId { get; }`, injected into AppDbContext's constructor and captured
by the query-filter lambdas, so EF re-evaluates it on every query
   ▼
Two things populate the mutable side (IMutableTenantContext, impl.
AmbientTenantContext, Infrastructure/Security):
  - TenantResolutionMiddleware (Api) — HTTP requests: copies the
    `tenant_id` JWT claim (absent entirely for a platform administrator)
    into the scope, right after UseAuthentication()
  - DeploymentWorker (Infrastructure) — background deployment jobs: the
    worker's manually-created DI scope has no HTTP context, so
    IDeploymentJobQueue now carries a `DeploymentJob(DeploymentId,
    TenantId)` record instead of a bare Guid, and the worker sets the
    scope's IMutableTenantContext from that before resolving
    IDeploymentExecutor
```

This is the entire isolation mechanism. No existing service's LINQ
queries changed — `db.Applications.Where(...)`, `db.Deployments...`, etc.
are all transparently scoped to whichever tenant is ambient in that
DbContext instance's DI scope, exactly like `IsActive` filtering would be
if this were an ordinary soft-delete pattern.

**The non-cascading query-filter caveat** (why every entity has its own
`TenantId`, not just the top-level ones): EF Core query filters do not
propagate through navigation/FK reachability by themselves. `AllowedDeploymentRoot`
is only reachable via `TargetServerId`, and `DeploymentLogEntry` only via
`DeploymentId` — if either table lacked its own `TenantId` + filter, a
cross-tenant row could still surface through a raw `db.AllowedDeploymentRoots`
or `db.DeploymentLogEntries` query even though the parent it points to is
correctly filtered. So every entity got its own column and its own filter,
independently.

### Tenant boundary details

- **`Permission`** — stays global, unscoped, no `TenantId` at all: the
  capability catalog is platform-wide, never owned by a tenant.
- **`User`/`Role`** — `TenantId` is **nullable**. Null means
  "platform-level": a platform administrator (`User.TenantId == null`,
  holding the single global `PLATFORM_ADMIN` role, `Role.TenantId ==
  null`) or a system-wide role definition. The *same* filter formula
  (`x.TenantId == currentTenantService.TenantId`) handles both cases
  without special-casing: a platform admin's JWT naturally carries no
  tenant claim, so their queries only ever match other null-`TenantId`
  rows — they see no tenant's data by default, which is the safe default,
  not an oversight. (A platform admin managing a specific tenant's data,
  if ever needed, is out of scope for this phase — not implemented.)
- **`Username`/`Email` stay globally unique**, not tenant-scoped — a
  deliberate exception. Login looks a user up by username alone, before
  any tenant is known (the tenant claim only exists once login has
  already resolved the user), so two tenants sharing a username would
  make login ambiguous. Enforced via a global unique index (unchanged)
  and via `UserService`/`TenantService`/`AuthService` explicitly calling
  `.IgnoreQueryFilters()` on that one lookup — documented inline at each
  call site. `AppDbContextExtensions.GetRolesAndPermissionsAsync` (used
  by login and several services to resolve one already-known user's own
  roles/permissions) also uses `IgnoreQueryFilters()`, for the same
  reason: without it, `ur.Role.Name` would apply `Role`'s filter mid-join
  and silently return zero roles for every tenant-scoped user during
  login, since no tenant is ambient yet at that point.
- **`Tenant` itself has no query filter** — it IS the isolation boundary,
  not a thing isolated by it. Visible only through
  `PermissionCodes.TenantsView`/`TenantsManage`, which are never granted
  to any tenant's own provisioned roles (`TenantService.CreateAsync`
  explicitly excludes both codes when granting the new tenant's ADMIN
  role "every permission" — a real bug caught by
  `TenantIsolationTests.TenantService_CreateAsync_NeverGrantsTheNewTenantsAdminRole_...`
  during this phase's own testing, fixed before merge).
- **Anonymous approval-preview endpoints**
  (`GetPromotionPreviewByTokenAsync`/`GetProductionApprovalPreviewByTokenAsync`,
  `[AllowAnonymous]`, Phase 8) also use `IgnoreQueryFilters()` — there is
  no tenant context on an unauthenticated request, and the 256-bit token
  itself (globally unique) is the sole, sufficient selector.
- **Previously-global unique indexes became tenant-scoped composites**:
  `ManagedApplication.Slug`, `Repository.Name`, `TargetServer.Name`,
  `BuildServer.Name`, `EnvironmentDefinition.Name` are now
  `(TenantId, X)` — two tenants can both have an application named
  "sample-app". `Role.Name` is `(TenantId, Name)` unique **plus** a
  Postgres partial unique index on `Name` filtered to `TenantId IS NULL`
  (Postgres treats `NULL != NULL`, so the composite alone would not stop
  two system-wide roles from sharing a name).
- **Tenant deactivation locks out its users at login** —
  `AuthService.LoginAsync` checks `Tenant.IsActive` for a tenant-scoped
  user (queried without `IgnoreQueryFilters`, since `Tenant` has no filter
  to begin with) and rejects with "This account's organization is
  inactive." if the tenant was deactivated via `TenantService.UpdateAsync`.

### Tenant provisioning (`TenantService.CreateAsync`)

One call creates: the `Tenant` row; the tenant's own copy of the default
role set (`RoleNames.All` — ADMIN/DEVOPS/DEVELOPER/QA/UAT/CTO, all
`IsSystem = true`, all scoped to the new `TenantId`); the tenant's own
pipeline `EnvironmentDefinition`s (DEV/QA/UAT/PRODUCTION); default
role→permission grants (`DefaultRolePermissions`, a new `Domain.Constants`
class — the single source of truth this phase extracted from what used to
be a private method on `DataSeeder`, now shared by nothing else needing
duplicating); the tenant's ADMIN role granted every permission **except**
`TenantsView`/`TenantsManage`; and one initial tenant-admin `User` who can
sign in immediately. This is a one-time provisioning step, not idempotent
seeding — a brand-new tenant has no pre-existing rows, so unlike
`DataSeeder` there is nothing to check for first, and none of it runs
inside the ambient ("no tenant yet, since the caller is a platform admin")
scope — every created row's `TenantId` is set explicitly to the new
tenant's `Id`.

`CreateTenantRequest` accepts an optional `InitialAdminPassword`; when
omitted, one is generated (`RandomPasswordGenerator`, extracted from
`DataSeeder`'s equivalent bootstrap logic and now shared by both) and
returned exactly once in `CreateTenantResponse.GeneratedPassword` — never
logged, never persisted in plaintext, never retrievable again.

### `DataSeeder` restructuring

`DataSeeder.SeedAsync` now seeds **global, platform-level data only**:
the `Permission` catalog, the single system-wide `PLATFORM_ADMIN` role
(`RoleNames.PlatformAdmin`, distinct from the per-tenant `RoleNames.Admin`
every tenant also gets its own copy of), and a bootstrap platform-admin
`User` (`TenantId = null`) if none exists yet. This runs with no tenant
ambient (the app has just started, no HTTP request exists), so every
query in it is naturally scoped to `TenantId == null` by the same global
query filters everything else uses — no special-casing needed. Everything
that used to live here for the non-admin roles (default role→permission
grants, the `EnvironmentDefinition` seed) moved to
`TenantService.CreateAsync`'s per-tenant provisioning, described above.

### Background worker tenant threading

`IDeploymentJobQueue.Enqueue`/`DequeueAsync` now carry a
`DeploymentJob(Guid DeploymentId, Guid TenantId)` record instead of a bare
`Guid` (`InMemoryDeploymentJobQueue`'s `Channel<T>` follows suit).
`DeploymentService.SaveAndEnqueueAsync` enqueues `deployment.TenantId`
alongside its id. `DeploymentWorker` resolves `IMutableTenantContext` from
its per-job scope and calls `SetTenantId(job.TenantId)` **before**
resolving `IDeploymentExecutor` — everything `IDeploymentExecutor` and its
dependencies (`SecretReferenceService`, `NotificationService`, etc.) query
afterward is correctly tenant-scoped, with zero changes to any of their
own code. `DeploymentLogEntry` rows written during execution
(`DeploymentExecutor`'s private `DeploymentLogWriter`) are stamped with
the same `TenantId` the `Deployment` itself carries.

### Organization-specific roles (master requirements §3)

`RoleService` gained `CreateAsync`/`UpdateAsync` (previously read-only:
`GetAllRolesAsync`/`GetAllPermissionsAsync` only — there was no way to
create a custom role at all before this phase). A created role is always
tenant-scoped (`TenantId` from the caller's own tenant, `IsSystem =
false`); `UpdateAsync` rejects any attempt to modify an `IsSystem` role
(`ForbiddenException`) — the seeded defaults are read-only, protecting
them from accidental modification while still letting an organization add
its own roles (e.g. a "RELEASE_MANAGER" role with a hand-picked
permission subset). Gated by the existing `roles.manage` permission — no
new permission code needed for this part.

### New/changed API surface

- `POST/GET /api/tenants`, `PUT /api/tenants/{id}`, `GET
  /api/tenants/{id}` — `tenants.view`/`tenants.manage`
  (`TenantsController`, `[RequirePermission(PermissionCodes.TenantsView)]`
  at the controller level, `TenantsManage` on the mutating endpoints).
- `POST /api/roles`, `PUT /api/roles/{id}` — `roles.manage` (new; GET
  endpoints unchanged).
- New permission codes: `tenants.view`, `tenants.manage` — platform-
  administrator-only, never assignable to any tenant's own role (see
  above).

### Configuration / Techbey-assumption cleanup

No hardcoded organization-specific values were found remaining in code at
the start of this phase beyond what tenant-scoping itself now
generalizes: git provider, registry, environments, deployment targets,
build provider, and notification provider were already provider-
abstracted interfaces from Phases 2/3/6/7/8 (`IGitProviderClient`,
`IBuildProvider`, `INotificationProvider`, `IContainerRuntimeProvider`).
What this phase specifically removed was the *global, singleton* nature
of the configuration those abstractions point at — `EnvironmentDefinition`,
`Role`, `TargetServer`, `Repository`, and `BuildServer` rows all now
belong to exactly one tenant instead of being shared platform-wide, so
each organization configures its own environments/deployment
targets/repositories/build servers/allowed-deployment-roots independently.

### Admin UI (frontend)

New pages under `frontend/src/pages/admin/`, routed under `/admin/*`,
gated by `RequirePermission` per route and an "Admin ▾" nav dropdown
(`Layout.tsx`) that only renders items the signed-in user actually holds
the view permission for:

| Page | Route | Permission | Notes |
|---|---|---|---|
| `TenantsPage` | `/admin/tenants` | `tenants.view`/`tenants.manage` | Platform-admin only. Create form provisions a tenant + initial admin in one step; shows the generated password exactly once. Activate/deactivate toggle. |
| `UsersPage` | `/admin/users` | `users.view`/`users.manage` | Tenant-scoped list + create form (username/email/password/roles) + activate/deactivate. |
| `RolesPage` | `/admin/roles` | `roles.view`/`roles.manage` | Lists roles with their granted permissions; create/edit forms for non-system roles only (system roles shown read-only). |
| `RepositoriesPage` | `/admin/repositories` | `repositories.view`/`repositories.manage` | List + create form + activate/deactivate. |
| `TargetServersPage` | `/admin/target-servers` | `targetservers.view`/`targetservers.manage` | List + create form + activate/deactivate; expandable per-server allowed-deployment-roots view/add form. |
| `BuildServersPage` (labeled "Integrations") | `/admin/integrations` | `buildservers.view`/`buildservers.manage` | List + create form + activate/deactivate. |

These are genuinely new UI surface — before this phase, Users/Roles/
Repositories/TargetServers/BuildServers were API-only (no frontend page
existed for any of them; even `ApplicationsListPage`, which does exist,
has no inline create/edit form and points admins at "the API/admin
tooling"). Never expose a secret: `AccessTokenEnvVarName`/
`ApiTokenEnvVarName` fields shown here are (as the API always returned)
only the *name* of a server-side environment variable, never a token
value — no field on any of these pages can ever display one, because the
API never sends one.

### Testing (master requirements §7 — tested aggressively, per instruction)

New `tests/DevOpsPortal.Tests/Services/TenantIsolationTests.cs`, 11 tests,
all sharing one physical InMemory database across two (or more) tenants
and flipping the ambient `FakeCurrentTenantService` between them — the
exact scenario the query filters exist to guard (many tenants' rows
living side by side in the same tables, same as production Postgres):
cross-tenant application list/get-by-id, same-slug-different-tenant (no
collision), cross-tenant deployments/logs (list, direct-by-id query),
cross-tenant repositories (list, same-name-different-tenant), cross-tenant
user listing/get-by-id + global username uniqueness, two tenants each
having their own non-colliding "ADMIN" role, a permission held in one
tenant never satisfying a check for a user in another, the
`TenantsManage`/`TenantsView` exclusion bug described above, a full
tenant-creation → login → JWT-permissions round trip, and tenant
deactivation locking out its users at login.

`TestDb.CreateInMemory()` (existing, used by ~30 other test files)
required no changes to keep working: `FakeCurrentTenantService.TenantId`
defaults to `Guid.Empty`, chosen specifically because it's also the
implicit C# default of every entity's non-nullable `TenantId` property
when a test constructs one inline without setting it — so the entire
existing suite's inline `new ManagedApplication { ... }`-style
constructions needed zero changes. The handful of places constructing
`User`/`Role` directly (nullable `TenantId`, defaults to `null`, which
does *not* match `Guid.Empty`) were updated to set it explicitly; a new
`TestDb.CreateInMemory(out FakeCurrentTenantService tenantContext)`
overload exists for tests that need to switch tenants mid-test.

**Verification**: all 325 backend tests pass (314 pre-existing + 11 new,
zero regressions), all 38 frontend tests pass, `dotnet ef migrations
has-pending-model-changes` reports none, a live `dotnet ef database
update` against a fresh Postgres 16 instance applied cleanly, and a live
`dotnet run` + `curl` session confirmed over real HTTP: two tenants each
creating an application with the identical slug (both succeed, no
collision); each tenant's application list showing only its own
application; a cross-tenant direct-by-id fetch returning 404; a tenant
admin attempting to list or create tenants returning 403; and a
cross-tenant username collision correctly returning 409. A Playwright
smoke pass over the built frontend (dev server + live API) additionally
confirmed: the Admin nav renders only permission-held items, all six
admin pages render without console errors, a tenant admin is shown the
"you don't have permission" page (not a crash) when navigating directly
to `/admin/tenants`, and creating a custom role through the `RolesPage`
form round-trips correctly (appears in the list immediately after
creation).

### Known limitations

- **No backfill/migration tooling for pre-existing single-tenant data.**
  This phase assumes a fresh deployment (consistent with how every prior
  phase's live verification used a fresh Postgres instance) — there is no
  tool to assign a `TenantId` to rows that existed before this migration.
  Applying this migration to a database with real pre-Phase-9 data would
  need a manual backfill (create one `Tenant` row, `UPDATE` every
  tenant-owned table's `TenantId` to that tenant's id) before the
  now-`NOT NULL` columns and FK constraints would accept it.
- **A platform administrator cannot browse into a specific tenant's data.**
  By design (see "Tenant boundary details" above) — not implemented as a
  deliberate scope decision, since master requirements didn't ask for it
  and it would need its own careful audit-logging/consent story to avoid
  becoming a backdoor around the isolation this phase exists to build.
- **`AllowedDeploymentRoot` create/update via the Admin UI is add-only** —
  `TargetServersPage` can list and add allowed roots but has no UI for
  editing/deactivating one (the API supports `PUT
  .../allowed-roots/{rootId}`; only the UI is incomplete here).
- **EF Core model-validation warning** (`Model.Validation[10622]`, logged
  every startup/migration in dev, not an error): "Entity 'Role' has a
  global query filter defined and is the required end of a relationship
  with the entity 'RolePermission'/'UserRole'." This is expected and
  intentional — `UserRole`/`RolePermission` are pure join tables with no
  `TenantId`/filter of their own, and navigating through them to a
  filtered `Role` is exactly the mechanism `AppDbContextExtensions.GetRolesAndPermissionsAsync`
  relies on (with an explicit `IgnoreQueryFilters()` for the one case —
  login — where the filter would otherwise hide a legitimate result).
  Confirmed safe by `TenantIsolationTests` and the full existing suite.

### Phase 9 addendum — branch promotion, credentials, environment-focused UI

Done, same branch/PR as Phase 9 above (not a new phase). Clarifies and
extends the promotion workflow, adds a "Credentials" capability, and
reworks the frontend into a sidebar-navigated, environment-focused SaaS
layout, per explicit follow-up instruction.

**Promote vs. deploy — confirmed, not changed.** The exact workflow asked
for (developer deploys DEV explicitly; "Go Ahead to QA/UAT/Production"
only promotes, never deploys; QA/UAT/Production each require their own
explicit Deploy click; Production additionally needs CTO approval before
its Deploy click is enabled) was already exactly what
`IDeploymentService`/`DeploymentService` implemented since Phase 3
(`RequestPromotionAsync` → `ApprovePromotionAsync` → separate
`DeployApprovedPromotionAsync`, `DeployToDevAsync` as DEV's only direct
entry point). Verified by re-reading the state machine and interface doc
comments; only the frontend button label changed ("Request {tier}
promotion" → "Go Ahead to {tier}") to match the requested terminology
exactly — no backend behavior changed.

**Git branch promotion** is now a first-class, traceable part of
`RequestPromotionAsync`, built on top of `ApplicationEnvironment.BranchName`
(already configurable per application/environment — no new configuration
surface needed) rather than inventing a parallel branch model:
`PromotionRequest` gained `FromBranch`/`ToBranch` (a snapshot, at request
time, of both environments' configured branch names) and
`BranchPromotionSucceeded`/`BranchPromotionDetail`. `IGitProviderClient`
gained `PromoteBranchAsync(repository, sourceBranch, targetBranch)`;
`GitLabProviderClient` implements it via GitLab's merge-request API
(create — or reuse an already-open one on 409 — then accept), requiring
the same `AccessTokenEnvVarName` write-capable token pattern
`GetLatestCommitAsync` already uses for auth. Never blocks the DB-level
workflow: a missing repository/branch config or a real Git failure (no
token, merge conflict, GitLab unreachable) is recorded on the
`PromotionRequest` and surfaced in the UI (`PromotionCard`), but the
promotion request itself still succeeds — same "external integration
failure never gates the workflow" principle Phase 3 established for
notifications and commit lookup. `BranchPromotionDetail` is
`LogSanitizer.Sanitize`d before storage, same as deployment log output.

**Credentials** ("Developers and QA frequently ask DevOps for
credentials") reuses `SecretReference`/`ISecretProvider` entirely rather
than adding a parallel entity — `SecretReference` gained four plain,
non-secret display columns (`Username`, `Host`, `Port`, `DatabaseName`);
the actual secret value still only ever lives behind `ISecretProvider`,
addressed by the existing `ProviderKey`/`StoreKey`. The one deliberate,
narrow exception to "no method returns a plaintext value to a
controller": `ISecretReferenceService.RevealAsync` / `POST
/api/secrets/{id}/reveal`, gated by a new `secrets.reveal` permission
**distinct from** `secrets.view` (view sees metadata only; reveal sees
the actual value — an org can grant one without the other), and audited
as `secret.revealed` with the same scope-description detail
`secret.referenced` already uses — never the value itself, never
`AuditLog`. `secrets.reveal` was added to the Developer/QA/UAT/DevOps
default role grants (alongside `secrets.view`) so the exact pain point
described ("frequently ask DevOps") is addressed by default, not just
made theoretically possible; an org can tighten this via the Roles UI.
The frontend's `CredentialsPage` never fetches a value until the viewer
explicitly clicks "Show password" (one credential at a time, via
`ActionButton`, never preloaded or cached).

**Environment-focused UI.** `EnvironmentsPage` (all four tiers at a
glance) is unchanged and remains the landing overview; a new
`EnvironmentDashboardPage` (`/environments/:tier`) gives each environment
its own fully separated view — applications currently deployed there,
their commit/status/requester, and (reusing `PromotionCard`, not a
duplicate) anything pending that environment's action — exactly the
per-environment example format specified (application → release/commit →
status → requested by → action). `Layout` was rewritten from a top nav
bar to a fixed, collapsible-on-mobile sidebar (dark `slate-900`, blue-600
active-item accent, grouped into primary nav / Environments (color-dot
per tier) / Administration), a closer professional-SaaS-dashboard
treatment per the Techbey-inspired direction requested — content area
(white cards on `slate-50`) is unchanged, so this was a navigation
restructuring, not a full component-library rewrite.

**A real, pre-existing CSS bug was caught by live Playwright verification
and fixed**, not merely reported: `index.css` had `a { color: inherit; }`
as plain, unlayered CSS. Tailwind v4 puts its utility classes in
`@layer utilities`; per the CSS Cascade Layers spec, *unlayered* rules
always beat *layered* ones regardless of selector specificity — so that
one line silently overrode every `text-*` color utility ever applied to
an `<a>`/`NavLink` anywhere in the app. It had been invisible because the
old top nav's white background happened to make the inherited near-black
body text readable by accident; the new dark sidebar made every nav
link's text render in the same color as its own background. Fixed by
moving the rule into `@layer base`, restoring the intended cascade
(utilities > base) everywhere in the app, not just the sidebar. Caught by
taking an actual screenshot during smoke verification, not just checking
HTTP status codes — worth calling out since it would not have surfaced
from either backend tests or a build/typecheck pass.

**New migration**: `Phase9b_BranchPromotionAndCredentials` (additive
only — four nullable columns on `PromotionRequests`, four nullable
columns on `SecretReferences`, three new global permission codes seeded
by the existing generic `PermissionCodes.All` loop). Verified with a live
`dotnet ef database update` against a fresh Postgres 16 instance and
`dotnet ef migrations has-pending-model-changes` (none).

**New tests**: `BranchPromotionTests` (4 — success/failure/no-repository/
no-branches-configured, all confirming the DB-level workflow is never
blocked) and four new cases in `SecretReferenceServiceTests` (reveal
requires `secrets.reveal` even with `secrets.view`; reveal returns the
correct value; reveal audits without the value; structured fields
round-trip through Create/Get). 333/333 backend tests pass (325 prior +
8 new), 38/38 frontend tests pass (2 existing `PromotionRequestDto` test
fixtures updated for the new fields).

**Known limitations (this addendum):**
- Branch promotion's merge-request-based approach means a genuine merge
  conflict is surfaced as a failure (`BranchPromotionSucceeded = false`,
  detail names the conflict) rather than auto-resolved — this is
  intentional (an automatic conflict resolution would be far riskier than
  reporting it), but there is no in-portal conflict-resolution UI; an
  operator resolves it directly in GitLab, same as they would today.
- `RevealAsync` reveals to anyone holding the (org-configurable)
  `secrets.reveal` permission platform-wide within their tenant — it is
  not further scoped per-environment/per-application the way deployment
  permissions are (matches the pre-existing `secrets.view`/`secrets.manage`
  model exactly; not a new limitation, just inherited).
- The sidebar redesign restructured navigation only; it did not touch
  `ApplicationsListPage`'s "manage via the API/admin tooling" placeholder
  or add inline create/edit forms to pages this addendum didn't otherwise
  touch (Applications itself still has no inline create form — unchanged
  from before this addendum, and out of the requested scope).

<<<<<<< HEAD
=======
## Phase 10 — Production Hardening, Security & Recovery

A review pass across the whole platform (Phases 1–9 plus the addendum
above), not a new feature phase: fixed confirmed gaps, left already-correct
behavior untouched, and made deliberately-deferred gaps (remote execution,
Release-based deployment, etc. — see "Production-critical gaps" below)
more visible rather than papering over them. No domain/entity changes, so
no new EF Core migration — `dotnet ef migrations has-pending-model-changes`
reports none, confirmed live against a fresh Postgres 16 instance.

**Branch note.** This phase's instructions said to branch from `main` on
the premise that "Phase 1–9 are complete and merged." At the time this
phase started, `main` contained Phase 1–9's multi-tenant work (PR #9,
merge commit `b56f0b7`) but **not** the Phase 9 addendum above (branch
promotion, Credentials, the sidebar redesign, the CSS layering fix) —
that commit had been pushed to `claude/phase-9-multi-tenant` after PR #9
was already merged, so it was never included in any pull request. Rather
than silently dropping that work by branching cleanly from `main`, this
phase's branch was built as `main` + that orphaned commit (cherry-picked
cleanly, zero conflicts, full test suite green immediately after), so the
addendum is finally included in a reviewable PR alongside Phase 10's
hardening. Everything below assumes that combined base.

### §1 Security review

Read through authentication, authorization, IDOR, path traversal, command
injection, SSRF, Docker operation safety, secret/log exposure, SQL
injection, file/Git/background-job safety, race conditions, CSRF, CORS,
and rate limiting. Most of this was already solid from earlier phases
(parameterized EF Core queries everywhere — no raw SQL in the codebase;
`ComposeCommandExecutor` uses `ArgumentList`, never a shell, so no command
injection surface; every controller is `[Authorize]` with either a static
`[RequirePermission]` or an equivalent in-service check — verified file by
file; bearer-token auth means CSRF doesn't apply; the frontend's nginx
reverse-proxies `/api/*` so the browser only ever sees one origin, which is
why no CORS policy was ever needed — confirmed still accurate).

**Fixed:**
- **No rate limiting anywhere, on any endpoint — most exploitable as an
  unlimited login brute-force/credential-stuffing surface.** Added
  ASP.NET Core's built-in rate limiter (`Microsoft.AspNetCore.RateLimiting`,
  already part of the shared framework — no new dependency): a global
  per-IP fixed-window policy (300 req/min) as a general abuse/resource-
  exhaustion guard, and a much stricter named `"auth"` policy (10 req/min
  per IP) applied to `POST /api/auth/login` specifically, since it's the
  highest-value automated-guessing target. Rejections return `429` with a
  small JSON body, consistent with the existing error-shape convention.
  Live-verified: 11th login attempt within a minute from the same IP
  returns 429 regardless of the credentials supplied; a successful login
  is throttled identically to a failed one (the bucket is per-IP, not
  per-outcome, so it can't be bypassed by alternating usernames).
- Everything else reviewed (IDOR via tenant-scoped global query filters +
  by-id ownership checks, approval-link tokens — 256-bit random, hashed at
  rest, constant-time-compared, never logged — GitLab token handling never
  logged, YAML analysis is pure in-memory parsing with no filesystem
  access, password hashing is PBKDF2-HMAC-SHA256 via ASP.NET Core
  Identity with an 8-character minimum) was already correct; no changes
  made there, per this phase's "no speculative rewrites" instruction.

### §2 Deployment safety

Verified: only a `TargetServer`/`ApplicationEnvironment` the application is
actually configured for can be deployed to; every promote/approve/deploy
action re-checks the environment-specific permission server-side (never
trusts a UI-hidden button); a Postgres partial unique index
(`WHERE "Status" IN (0,1,2)`, i.e. Pending/Queued/Running) plus an
application-level pre-check together prevent two concurrent deployments to
the same application/environment even under a check-then-insert race;
rollback only accepts a previously-`Succeeded` deployment of the *same*
application+environment as its target and goes through the identical
queued/concurrency-guarded path as a normal deployment (no shortcut).

**Fixed:**
- **No execution timeout.** `DeploymentWorker` processes one job at a
  time; a single stuck `docker compose up` (an image pull that hangs
  forever, for example) would have blocked every subsequent deployment
  indefinitely with no way to recover except restarting the process.
  `DeploymentExecutor` now bounds the whole compose-command-plus-health-
  check sequence with a linked, cancellable timeout — configurable via
  `Deployment:ExecutionTimeoutMinutes` / `DEPLOYMENT_EXECUTION_TIMEOUT_MINUTES`
  (default 20 minutes) — and on expiry marks the deployment `Failed` with
  an explicit "Deployment timed out after N minute(s)." reason rather than
  hanging the worker loop. Covered by a new test using a compose executor
  that never completes on its own.

### §3 Database review

Reviewed indexes, foreign keys, cascade behavior, and uniqueness
constraints across every entity in `AppDbContext`. Findings: FK
`DeleteBehavior` is `Restrict` everywhere a delete could silently discard
meaningful history (deployments, promotions, audit-adjacent data), and
`Cascade` only where that's actually correct (a `Deployment`'s own log
entries, a `PromotionRequest`'s own `ProductionApproval`) — no changes
needed, this was already deliberate. Composite/filtered unique indexes
(tenant-scoped names/slugs, the active-deployment concurrency guard, the
approval-token-hash indexes) are all present and correctly scoped.
`AuditLog`/`Deployment`/`DeploymentLogEntry` already carry the indexes
their query patterns need (`Timestamp`, `UserId`, `Action`,
`(ApplicationId, EnvironmentDefinitionId, Status)`, etc.).

**Fixed:**
- **Unbounded deployment-history growth with no query cap.**
  `DeploymentService.ListAsync` had no `Take(...)` and no caller-supplied
  paging — every deployment ever created for a heavily-deployed
  application would be pulled and serialized on every unfiltered call
  (the dashboard/environment views call this with no filters). Added a
  `Take(500)` cap (most-recent-first, same ordering as before); callers
  that need a narrower slice already have `applicationId`/
  `environmentDefinitionId`/`status` filters available. Covered by a new
  test seeding 505 deployments and asserting the result is capped at 500.
  `AuditController`/`AuditService.QueryAsync` already had proper
  page/pageSize paging (clamped 1–200) — no change needed there.
- Added `EnableRetryOnFailure(maxRetryCount: 3)` to the Npgsql connection
  (no manual `BeginTransaction` calls exist anywhere in the codebase, so
  this is safe to enable with no execution-strategy conflicts) — a
  transient network blip or a Postgres restart no longer takes the whole
  API down with it.

**Noted, not changed:** EF Core's model-validation pass emits a design-time
warning that `Role`'s global query filter is the required end of a
relationship with `RolePermission`/`UserRole`. This predates Phase 10 (it
was already present after Phase 9's tenant-isolation work) and
`TenantIsolationTests` plus the full 340-test suite already pass with it —
changing global query filter wiring is exactly the kind of
architecture-level change this phase's instructions said not to make
speculatively. Left as a known, harmless-in-practice warning for a future
phase to address if it ever proves to matter in practice.

### §4 Backup & recovery

No backup mechanism existed at all before this phase. Added
`scripts/backup-database.sh` and `scripts/restore-database.sh` —
deliberately infra-agnostic (every location/credential comes from standard
libpq `PG*` environment variables plus `BACKUP_DIR`/
`BACKUP_RETENTION_DAYS`; nothing hardcodes a host, container name, or
filesystem path), so they work whatever's actually reaching the portal's
Postgres instance in a given environment.

- **Backup**: `pg_dump --format=custom` (compressed, supports selective/
  parallel restore) to a timestamped file, immediately followed by
  `pg_restore --list` against the new archive to verify it's structurally
  readable before declaring success (a corrupt/truncated dump is deleted
  and the script exits non-zero rather than leaving a false sense of
  safety on disk). Prunes backups older than `BACKUP_RETENTION_DAYS`
  (default 14) in its own directory.
- **Restore**: `pg_restore --clean --if-exists --no-owner`, requires an
  explicit `--yes` flag (a restore is destructive against whatever
  database `PGDATABASE` currently names — this never guesses that a human
  meant to overwrite production) and prints the follow-up verification
  steps (`dotnet ef migrations has-pending-model-changes` + an application
  smoke test) rather than declaring the restored database live on its own
  say-so.
- **Recommended schedule**: daily via cron/systemd timer (the doc comment
  in `backup-database.sh` includes an example crontab line);
  `BACKUP_RETENTION_DAYS=14` locally, with whatever longer-term/offsite
  copy policy the surrounding infrastructure already uses for other
  stateful services layered on top (this script manages its own local
  directory's retention only — it is not a substitute for an offsite/3-2-1
  policy).
- **Restore verification procedure** (documented in the restore script's
  own header, not just implied): run the restore script against a
  throwaway/staging database after every backup rotation — not only when
  an incident forces an actual restore — and confirm the application
  starts against it with no pending migrations. This is the only way to
  actually know a backup is restorable rather than merely present on disk.
- Live-verified end-to-end against a real Postgres 16 instance in this
  session: backup a database with real data → verify archive → restore
  into a fresh target database → confirm the data round-trips exactly.

### §5 Audit coverage

Reviewed every `IAuditService.LogAsync` call site across every service.
Coverage was already comprehensive — every create/update/delete on every
security-sensitive entity (`User`, `Role`, `Tenant`, `SecretReference`,
`TargetServer`, `Repository`, `Deployment`, `PromotionRequest`,
`ProductionApproval`, container operations, builds), every auth outcome
(success, invalid credentials, inactive account, inactive tenant),
password reset/change, and the addendum's `secret.revealed` action are all
audited today. No gaps found; no changes made.

### §6 Observability & error handling

`ExceptionHandlingMiddleware` already maps every exception type to the
correct HTTP status and a clean `{ "error": "..." }` body, and only logs
(never returns to the caller) the exception detail/stack trace for the
unmapped-`500` case — confirmed this was already correct, no stack trace
or internal detail is ever exposed to a client. `LogSanitizer` already
redacts deployment log output; `DeploymentExecutor` additionally
replaces every literal resolved-secret-value occurrence beyond
pattern-based redaction. No changes made here — this was already
production-safe.

### §7 Health endpoints

Only a single combined `/health` (API + DB) existed before this phase.
Added granular endpoints, all still unauthenticated by design (component
status strings only, nothing sensitive in the response) and all sharing a
structured JSON response writer (`{ status, totalDurationMs, checks: [...] }`)
instead of the framework's default plain-text body:

- **`/health`** — everything, for a simple all-in-one probe (unchanged
  route, now with the structured body and the two new checks below).
- **`/health/live`** — zero dependencies (`Predicate = _ => false`),
  answers "is the process up" for an orchestrator restart policy.
- **`/health/ready`** — database + background worker: "can this instance
  actually serve deployments" (deliberately excludes `integrations`, since
  SMTP/remote-execution being unconfigured is a supported, non-blocking
  state, not a readiness failure).
- **`/health/db`**, **`/health/worker`**, **`/health/integrations`** —
  each component in isolation, for targeted troubleshooting.
- New `BackgroundWorkerHealthCheck` inspects `DeploymentWorker`'s
  `BackgroundService.ExecuteTask` (public since .NET Core 3.0) rather than
  a separate heartbeat mechanism — reports unhealthy if the worker isn't
  registered, hasn't started, or its loop has completed/faulted.
- New `IntegrationsHealthCheck` reports SMTP and remote-execution-provider
  configuration state as `Degraded` (never `Unhealthy` — an unconfigured
  optional integration is a documented, supported state, not an outage;
  see `NotConfiguredRemoteExecutionProvider`'s own doc comment). Never
  makes a live network call — that would make every health check
  slow/flaky — just reports configuration presence.
- Live-verified end-to-end (all six endpoints, correct status per
  endpoint, `/health/ready` correctly excluding the degraded-but-non-
  blocking integrations check) and covered by new unit tests for both new
  `IHealthCheck` implementations.

### §8 Performance review

Reviewed database queries, dashboard/environment-view aggregation, log
retrieval, and the background worker for obvious bottlenecks.
`ApplicationService`/`ApplicationsController` already fetch with a single
`Include`, no N+1; `AuditService.QueryAsync` already paginates; permission
checks are a pure JWT-claim check with no DB round-trip per request. The
two real findings — unbounded `DeploymentService.ListAsync` and no Npgsql
retry policy — are fixed under §3 above (both are as much a performance
concern as a database-hygiene one, so documented once rather than twice).

### §9 Testing

340/340 backend tests pass (334 prior + 6 new: 1 deployment-timeout test,
1 deployment-history-cap test, 4 new health-check unit tests), 38/38
frontend tests pass (untouched by this phase). `dotnet build` and
`npm run build` both clean. `docker compose config` validates cleanly with
the new `Deployment__ExecutionTimeoutMinutes` variable wired through
end-to-end from `docker-compose.yml` → `appsettings.json` default. Live
Postgres 16 verification covered: `dotnet ef migrations
has-pending-model-changes` (none), full API smoke run (seed, health
endpoints, rate-limited login), and a full backup/restore round-trip.

**Known limitations (this phase):** no automated CI/scheduled execution of
the backup script is wired up (it's provided as a script + documented
schedule, not a running cron job — hosting environments differ too much to
hardcode one); the `Role` query-filter design-time warning noted under §3
is unresolved (deliberately, see that section); the rate limiter is
in-process/per-instance (a future multi-instance API deployment would need
a distributed limiter — out of scope until the portal is actually
horizontally scaled, which nothing today requires).

>>>>>>> main
## Production-critical gaps / next implementation

Carried forward, unresolved, and deliberately **not** touched by Phase 9
(multi-tenancy is orthogonal to these — they apply equally inside a
single tenant and were not hidden, removed, or worked around while making
the product multi-tenant):

1. **The portal is designed to run on a separate VM from the
   TargetServers it deploys to, but no secure remote-execution mechanism
   exists yet.** `IRemoteExecutionProvider`'s only implementation is
   `NotConfiguredRemoteExecutionProvider` (Phase 5), which honestly
   reports every target server unreachable rather than pretending to
   operate one. **`DeploymentExecutor` (Phase 3) still runs `docker
   compose` locally, on whatever host the portal API process itself runs
   on** — it does not reach out to a `TargetServer` over any remote
   channel. This was true before Phase 9 and remains true after it; Phase
   9 added tenant-context threading to `DeploymentWorker`/
   `DeploymentExecutor` (see above) without touching this gap.
2. **No fake/local implementation has been substituted for real remote
   execution**, and none should be — `NotConfiguredRemoteExecutionProvider`
   staying honest about "unreachable" is the correct behavior until a real
   mechanism (SSH, a remote Docker API over TLS, an agent process on each
   TargetServer, etc.) is built and wired through the *same*
   `IRemoteExecutionProvider`/`IContainerRuntimeProvider` abstraction
   Phase 5 already defined for this purpose.
3. **The same remote-execution abstraction is meant to eventually serve**
   deployment execution (`DeploymentExecutor`), container monitoring
   (`ContainerOperationsService.GetStatusAsync`), restart/start/stop
   (`ContainerOperationsService`), and `docker compose down -v`/`up -d`
   recreate — all four already call through `IContainerRuntimeProvider`
   today (Phase 5), so wiring a real provider implementation is the only
   remaining step for all four at once; no per-operation redesign needed.
4. **Phase 6's Jenkins build pipeline produces real, immutable `Release`
   rows, but nothing deploys from one yet.** `IDeploymentService` has no
   "deploy from Release" request path — only Legacy (prebuilt publish
   directory) and the original commit-SHA-based Modern-path requests
   exist. A `Release`'s `ImageReference` is never consumed by
   `DeploymentExecutor`.
5. **Phase 7's secret resolution integrates with the existing (local,
   Legacy-path) deployment execution flow, but a ContainerImage/`Release`
   deployment's environment-specific secret injection at execution time
   is unbuilt** — `SecretReferenceService.ResolveForDeploymentAsync` is
   wired into `DeploymentExecutor`'s current compose-based execution path
   only; once (4) exists, image-based deployments will need the same
   secure, redacted, process-env-var-only injection Legacy deployments
   already get.
6. **Session storage remains sessionStorage-based** (flagged since Phase
   4), not an httpOnly cookie — unrelated to multi-tenancy, still open.
7. **No backfill tooling for pre-Phase-9 single-tenant data** (see Phase
   9's own "Known limitations" above) — relevant specifically because any
   future work that assumes production data already exists needs this
   solved first.

None of the above was implemented, hidden, or silently worked around in
Phase 9 — this section exists so the gap stays visible and explicit
rather than getting lost once multi-tenancy makes the codebase look more
"finished" than the actual deployment-execution path is.

## Next phase

Not yet assigned — Phase 10 (Production Hardening, Security & Recovery) is
complete; awaiting explicit approval before starting further work.
Strongest candidate, per the "Production-critical gaps" section directly
above (unchanged by Phase 10 — hardening the existing surface area
deliberately did not touch these architecture-level gaps): a real secure
remote-execution mechanism for `IRemoteExecutionProvider`, since it blocks
the portal's own stated target architecture (Portal VM → secure remote
execution → Target Server → Docker Compose) and would immediately benefit
deployment execution, container monitoring, and container control all at
once. Other candidates, unchanged from before: wiring `IDeploymentService`/
`DeploymentExecutor` to deploy from a `Release` (Phase 6) and secret
injection for that path (Phase 7), broader notification distribution or a
second `INotificationProvider` (Phase 8), a platform-admin
view-into-a-tenant capability (Phase 9, deliberately deferred), backfill
tooling for pre-Phase-9 data, hardening session storage to an httpOnly
cookie (flagged since Phase 4), or wiring the new backup script into an
actual scheduled job for a given hosting environment (Phase 10, provided
as a script + documented procedure rather than a hardcoded schedule — see
Phase 10 §4/§9). Do not assume which without asking.
