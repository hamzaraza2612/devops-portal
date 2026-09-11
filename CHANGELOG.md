# Changelog

Versioning: [SemVer](https://semver.org/). The running version is shown in
`GET /health`'s `version` field. See the
[Installation Guide](docs/installation-guide.md#upgrading) for the upgrade
procedure — in short: back up first, `git pull` / pull the new image,
`docker compose up -d --build`. Every migration below is additive-only
(new nullable columns/tables); none has ever dropped or destructively
altered existing data.

## v1.2.0 — Configuration CRUD & Environment Server Dashboard host metrics

Closes the remaining "DevOps has to edit the database to configure the
portal" gaps: every configuration/resource type the portal manages now has
full Create/View/Edit/Delete coverage in the UI (previously several had
create-only or view-only screens), and the "Test Connection" check against
a `TargetServer` now also reports host-level uptime/load, memory, and disk
— the "Environment Server Dashboard" data master requirements asked for,
alongside the SSH/Docker/Compose status that already existed.

**Highlights**:
- **Applications**: full Create/Edit/Delete/Deactivate from the
  Applications list (previously view-only with a "manage via the
  API/admin tooling" placeholder).
- **Application environment configuration**: the target server, deployment
  paths, compose/service/container names, health check, and application
  URL for each application × environment pairing can now be created,
  edited, and removed directly from the application's details page
  (previously there was no UI for this at all — the biggest gap closed
  this release).
- **Repositories, Target Servers, Secrets, Users**: Edit and Delete (or
  Deactivate, where a hard delete isn't safe — see below) added everywhere
  Create already existed but Edit/Delete didn't; Secrets also gained a
  "Rotate value" action distinct from editing metadata.
- **Environments admin page**: a new `/admin/environments` page to view
  and edit the bounded, safe subset of environment-stage configuration
  (`IsProductionLike`, `IsActive`) — see "Known limitations" for why Name/
  SortOrder/full Create/Delete are deliberately not offered.
- **Environment Server Dashboard host metrics**: "Test Connection" now
  also runs `uptime`, `free -h`, and `df -h` on the target server and
  reports the raw output alongside the existing SSH/Docker/Compose
  status — never fabricated: any command that fails independently reports
  as unavailable rather than failing the whole test or inventing a value.

**Delete semantics** (unchanged behavior, now exposed in the UI): most
entities (`TargetServer`, `Repository`, `User`, `Secret`) already used to
deactivate rather than hard-delete; this release adds real `DELETE`
endpoints only where a row is either safe to hard-delete unconditionally
(`Repository` — any reference is cleared, not cascaded) or where deletion
is guarded by a live check for dependents and refused with a clear error
otherwise (`ApplicationEnvironment`, `ManagedApplication`, `TargetServer`
— all blocked while deployment/config history references them). Deleting
a configuration record never touches the actual remote server, its Docker
containers, or application data — it only removes the portal's own
reference.

**Known limitations**: `EnvironmentDefinition` (the four pipeline stages)
deliberately does not get a full Create or hard-Delete — their `Name` and
`SortOrder` are load-bearing, hardcoded by exact string throughout both
the backend's permission-synthesis/deployment-service dictionaries and the
frontend's environment-tier dictionaries; adding a fifth stage through a
generic Create would silently grant it zero permissions rather than fail
loudly, so only `IsProductionLike`/`IsActive` are editable until a broader
change makes the pipeline stages themselves data-driven. As in v1.1.0, no
real DEV/QA/UAT/PRODUCTION target server was reachable from this release's
own build environment to exercise the new host-metrics commands against;
the SSH connect/auth/command-execution code path itself (not just the
`ssh` CLI) was validated against a local loopback `openssh-server`
installed for that one-off check — see `PROJECT_STATE.md`'s Phase 13
section for exactly what that did and didn't prove, and for the manual
verification steps to run against real infrastructure.

## v1.1.0 — Real remote execution, GitLab integration & simplified authorization

Closes the two biggest gaps called out in v1.0.0's known limitations:
deployment execution/container monitoring now genuinely reach a remote
`TargetServer` over SSH instead of always running locally/reporting
unreachable, and GitLab connectivity is real and testable from the UI
instead of a placeholder. Also removes multi-tenancy and the Role/
Permission catalog entirely, replacing them with a simpler
`User.IsAdmin` + `User.CanApproveProduction` + per-environment
`UserEnvironmentAccess` model — see `PROJECT_STATE.md`'s "Phase 12"
section for the full record.

**Breaking changes**: the `Tenants`, `Roles`, `Permissions`, `UserRoles`,
and `RolePermissions` tables are dropped by this release's migration —
any pre-existing multi-tenant deployment of this platform (none are known
to exist outside this project's own history) would need a manual data
migration before upgrading; there is no automated backfill tool. A fresh
install is unaffected. Every user must now have `IsAdmin` set or at least
one `UserEnvironmentAccess` row to do anything — the bootstrap admin
account is seeded with `IsAdmin = true` as before, so a fresh install's
first login is unaffected.

**Highlights**:
- **Remote execution**: `TargetServer` now holds SSH connection details
  (hostname/port/username/auth method + a securely-stored credential) and
  a "Test Connection" action; LegacyFilesystem deployment execution and
  all container monitoring/control operations run over SSH against the
  configured target server.
- **GitLab integration**: `Repository` now holds a securely-stored access
  token and a "Test Connection" action; branch promotion and commit
  lookup use it for real instead of degrading to "not reachable."
- **Deploy from Release**: a ContainerImage-mode application can now
  actually be deployed from one of its own built `Release`s (image
  pull + `compose up -d` on the target server).
- **Simplified authorization**: multi-tenancy and the Role/Permission
  catalog are gone; every permission check is unchanged in code, only
  what grants it changed (see `PROJECT_STATE.md`).
- **Container CPU/memory stats and logs**: container monitoring now shows
  live CPU%, memory usage/limit/%, and PID count per container, plus an
  on-demand log viewer (`docker logs --tail N`, configurable tail lines,
  manual refresh) — found missing during a pre-merge acceptance review
  and closed in the same release rather than shipped as a known gap.

**Migration notes**: one new migration
(`Phase12_RemoveMultiTenancyAndRbac`) — applied automatically on first
startup like every migration before it.

**Known limitations**: no live SSH server, GitLab instance, or Docker
daemon was available to exercise the new remote-execution/GitLab paths
against real infrastructure in this release's own environment — see
`PROJECT_STATE.md`'s Phase 12 "Known limitations" for exactly what was
verified and the recommended manual verification steps before production
reliance. A broader frontend visual/UX polish pass was deliberately
deferred in favor of this release's remote-execution/authorization work.

## v1.0.0 — First production release

The complete platform, covering the full application lifecycle end to end:
core auth/RBAC, deployment configuration, the DEV→QA→UAT→Production
promotion engine, a web UI, container monitoring abstractions, a Jenkins
build pipeline, encrypted secrets management, email notifications with
secure approval links, multi-tenancy, and a production-hardening pass
(rate limiting, deployment timeouts, granular health checks, backup
tooling, a full documentation set).

**Migration notes**: this release includes every migration from
`InitialCreate` through the multi-tenant/credentials/branch-promotion
migrations — a fresh install applies all of them automatically on first
startup (see the [Installation Guide](docs/installation-guide.md)). There
is no separate "v1.0.0 migration" to run by hand.

**Upgrade notes**: if you were running a pre-release build of this
platform, back up your database (see the
[Installation Guide](docs/installation-guide.md#backup)) before upgrading,
then follow the standard [upgrade procedure](docs/installation-guide.md#upgrading).
No manual data migration or configuration change is required beyond the
standard `.env` variables already documented in the
[Installation Guide](docs/installation-guide.md#3-configure-environment-variables) —
if you're upgrading from before the deployment-execution-timeout feature,
review `Deployment:ExecutionTimeoutMinutes` (defaults to 20 if unset) and
adjust if your deployments legitimately take longer.

### Highlights by area

- **Core**: JWT auth, RBAC (permission-based, not role-name-based),
  audit log, health endpoints.
- **Deployment engine**: DEV→QA→UAT→Production promotion pipeline with
  explicit, separately-authorized approve/deploy actions at every step;
  CTO-gated production approval; rollback; a database-enforced concurrency
  guard; a configurable execution timeout.
- **UI**: full React SPA covering the deployment workflow, environment
  dashboards, deployment history/logs, and admin screens for
  tenants/users/roles/repositories/deployment targets/build servers.
- **Container operations**: status/restart/recreate abstraction, correctly
  honest that no real remote-execution mechanism exists yet (see
  [Architecture: Known gaps](docs/architecture.md#known-gaps)).
- **Build pipeline**: Jenkins integration producing immutable, traceable
  `Release` records (image deploy-from-release is schema-ready but not yet
  execution-wired — see Known gaps).
- **Secrets**: AES-256-GCM encryption at rest, environment-scoped
  resolution, a narrow separately-permissioned "reveal" action, never
  logged or returned by any other endpoint.
- **Notifications**: approval-request and deployment-outcome emails with
  secure, expiring, read-only preview links (never a bypass to actually
  approve anything).
- **Multi-tenancy**: full tenant isolation via database-level query
  filters, per-tenant role/environment provisioning, a platform-admin tier
  separate from any tenant's own data.
- **Production hardening**: login rate limiting, Npgsql retry-on-failure,
  a bounded deployment-history query, backup/restore tooling with archive
  verification, granular `/health/*` endpoints, a fixed unhandled-500 on
  genuine deployment-concurrency races (now a clean 409).
- **Documentation**: the full guide set linked from the root `README.md`.

### Known limitations

See [Architecture: Known gaps](docs/architecture.md#known-gaps) for the
complete, current list — most notably: deployment execution runs on the
portal's own host rather than reaching a genuinely remote target server
over the network yet, and there's no deploy-from-Release path. Neither is
hidden; both fail honestly rather than fabricating success.
