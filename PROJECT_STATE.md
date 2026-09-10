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

## Next phase

Not yet assigned — Phase 2 (Legacy Deployment Discovery & Configuration) is
complete; awaiting explicit approval before starting further work. Natural
candidates per the original phase plan: GitLab live integration (branch/commit
listing via the GitLab API), or the deployment execution engine that
actually acts on `ApplicationEnvironment` config (clone/sync/backup/restart
for legacy-filesystem mode). Do not assume which without asking.
