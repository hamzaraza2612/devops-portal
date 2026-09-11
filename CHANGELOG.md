# Changelog

Versioning: [SemVer](https://semver.org/). The running version is shown in
`GET /health`'s `version` field. See the
[Installation Guide](docs/installation-guide.md#upgrading) for the upgrade
procedure — in short: back up first, `git pull` / pull the new image,
`docker compose up -d --build`. Every migration below is additive-only
(new nullable columns/tables); none has ever dropped or destructively
altered existing data.

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
