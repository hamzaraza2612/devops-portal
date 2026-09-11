# Developer Guide

Setting up a local dev environment and extending the platform. See
[Architecture](architecture.md) for the system design this builds on.

## Local setup

Requires .NET 8 SDK, Node 22+, and a local PostgreSQL instance (or point
`ConnectionStrings__Default` at any reachable Postgres 16+).

```bash
# Backend
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=devopsportal_dev;Username=postgres;Password=postgres"
export Jwt__SigningKey="a-local-dev-signing-key-at-least-32-bytes"
export Secrets__EncryptionKey="a-local-dev-encryption-key-at-least-32-bytes"
dotnet run --project src/DevOpsPortal.Api
```

Migrations apply automatically on startup, same as production. A bootstrap
admin user is created if none exist — check the console output for the
generated password if `Seed__AdminPassword` isn't set.

```bash
# Frontend (separate terminal)
cd frontend
npm install
npm run dev
```

Vite's dev server proxies `/api/*` to `http://localhost:5199` by default
(see `frontend/vite.config.ts`) — no separate CORS setup needed.

## Running tests

```bash
dotnet test                          # backend — EF Core InMemory, no DB needed
cd frontend && npm run test -- --run # frontend — Vitest + React Testing Library
```

Both suites run with zero external dependencies (no live database, no
network calls) — this is deliberate so CI and local dev never depend on
infrastructure being available.

## Adding a new provider implementation

The pattern is the same for every abstraction in the table in
[Architecture](architecture.md#provider-abstractions):

1. Implement the interface (e.g. a new `IGitProviderClient` for GitHub) in
   `Infrastructure/<Area>/`. Never throw for an expected external failure
   (network, auth, 404) — return the abstraction's own `Result.Fail(...)`
   type instead, so a provider outage can never break the calling
   workflow. Look at `GitLabProviderClient` or `JenkinsBuildProvider` as a
   worked example of this pattern.
2. Register it in `Infrastructure/DependencyInjection.cs`.
3. Nothing in `Application` or `Api` changes — every caller already goes
   through the interface.

## Adding a new permission-gated action

1. Add the permission code to `Domain/Constants/PermissionCodes.cs`.
2. Add it to whichever default roles should get it in
   `Domain/Constants/DefaultRolePermissions.cs` (seeded automatically —
   the generic "seed every `PermissionCodes.All` entry" loop in
   `DataSeeder` picks up new codes with no other seeder change needed).
3. Enforce it either via `[RequirePermission(...)]` on the controller
   action (for a static, environment-independent permission) or inside the
   service via `EnsurePermissionAsync` (for anything environment-dependent,
   like the QA/UAT/Production-specific deploy/approve/promote permissions —
   see `DeploymentService` for the pattern). Prefer the in-service pattern
   when in doubt — it's directly unit-testable and exercised the same way
   regardless of which controller route reaches it.

## Database migrations

```bash
cd src/DevOpsPortal.Api
dotnet ef migrations add <Name> --project ../DevOpsPortal.Infrastructure
dotnet ef migrations has-pending-model-changes  # verify before committing
```

Every migration in this codebase to date is **additive only** — new
nullable columns, new tables, new indexes — never a destructive rename or
drop against existing data. Keep it that way; a destructive migration
against a production tenant's history/audit data is a much bigger decision
than a schema change and should be treated as one explicitly, not slipped
into a routine feature migration.

## Code conventions

- No comments explaining *what* code does — names should already do that.
  A comment is for a non-obvious *why* (a workaround, a deliberate
  tradeoff, a security invariant) — see the existing codebase for the
  house style; it's thoroughly, consistently commented in exactly this way.
- Permission checks belong server-side, always — a frontend `can()` check
  only decides what to *show*, never what to *allow*.
- Never log or return a secret value — see
  [Architecture: Security posture](architecture.md#security-posture).
- Run `dotnet build`, `dotnet test`, `npm run build`, and
  `npm run test -- --run` before every commit; all four are expected to
  pass cleanly at all times on `main`.

## Where to look next

- [Architecture](architecture.md) — system design and current gaps.
- `PROJECT_STATE.md` (repository root) — the full phase-by-phase design
  history, including the reasoning behind decisions this guide states
  without re-explaining.
