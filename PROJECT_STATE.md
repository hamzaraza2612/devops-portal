# PROJECT_STATE

## Current architecture

.NET 8 solution, clean/layered architecture:

```
src/DevOpsPortal.Domain          entities, enums, constants — no dependencies
src/DevOpsPortal.Application     DTOs, service interfaces + implementations,
                                  IAppDbContext abstraction (EF Core-typed but
                                  provider-agnostic)
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

## Current database state

PostgreSQL via EF Core migrations (`src/DevOpsPortal.Infrastructure/Persistence/Migrations`,
`InitialCreate`). Tables: `Users`, `Roles`, `Permissions`, `UserRoles`
(join), `RolePermissions` (join), `AuditLogs`. Unique indexes on
`Users.Username`, `Users.Email`, `Roles.Name`, `Permissions.Code`.

Migrations apply automatically at API startup (`DataSeeder.SeedAsync` calls
`Database.MigrateAsync()`), followed by idempotent seeding of the 6 system
roles (ADMIN, DEVOPS, DEVELOPER, QA, UAT, CTO — `Domain.Constants.RoleNames`)
and the permission catalog (`Domain.Constants.PermissionCodes`). ADMIN is
granted all permissions; other roles get none by default in Phase 1 (every
authenticated user can still call `/api/auth/me` and
`/api/users/me/change-password`). A bootstrap admin user is created only if
the `Users` table is empty; its password comes from `Seed:AdminPassword`
(env `ADMIN_INITIAL_PASSWORD`) or, if unset, a random password logged once
as a warning.

## Implemented features

- **Auth**: `POST /api/auth/login` (username+password → JWT), `GET /api/auth/me`.
  Passwords hashed with ASP.NET Core Identity's `PasswordHasher<T>`
  (PBKDF2-HMAC-SHA256). JWT carries role claims and a `permission` claim per
  granted permission code; signed HS256 with `Jwt:SigningKey` (must be ≥32
  bytes, no default — app refuses to start without it).
- **RBAC**: permission-based, enforced server-side via a custom
  `IAuthorizationPolicyProvider` that resolves `"Permission:<code>"` policies
  on demand (no per-permission registration needed as new codes are added in
  later phases) plus `[RequirePermission(code)]` attribute. Roles/permissions
  are read-only via API in Phase 1 (assignment happens through user
  create/update); mutating the role→permission catalog itself is deferred.
- **Users**: `GET/POST /api/users`, `GET/PUT /api/users/{id}`,
  `POST /api/users/{id}/reset-password` (admin), `POST /api/users/me/change-password`
  (self-service). Enforces unique username/email, min 8-char passwords, ≥1
  role per user.
- **Roles**: `GET /api/roles`, `GET /api/roles/permissions` (read-only, `roles.view`).
- **Audit**: every login attempt (success/failure), user create/update/password
  change/reset logged with who/what/when/where(IP)/result. `GET /api/audit`
  (paged, filterable by user/action/date, `audit.view` permission). Never
  logs secret values.
- **Health**: `GET /health` (DB connectivity check) — used by the Docker
  Compose healthcheck (bash `/dev/tcp` probe; base runtime image has no
  curl/wget and none is installed, to keep the image minimal).

## Important configuration

Env vars (see `.env.example`): `POSTGRES_*`, `ConnectionStrings__Default`,
`Jwt__SigningKey` (required, ≥32 bytes), `Jwt__Issuer`/`Jwt__Audience`,
`Jwt__ExpiryMinutes` (default 480), `Seed__AdminUsername`/`Seed__AdminEmail`/
`Seed__AdminPassword`. No secrets are committed; `appsettings.json` ships
empty placeholders and the app fails fast at startup if `Jwt:SigningKey` is
missing/too short.

## Known issues / deliberate Phase-1 scope cuts

- Role→permission catalog mutation is not exposed via API yet (roles are
  fixed-seeded; only user→role assignment is mutable). Add when a phase
  needs custom roles.
- No refresh tokens — single JWT with a configurable expiry
  (`Jwt:ExpiryMinutes`, default 8h). Fine for Phase 1; revisit if session
  UX needs improve.
- `script.sh` and representative existing Docker Compose files (mentioned in
  the master requirements for the legacy filesystem deployment mode) were
  not present in this repository or environment — needed before Phase 4
  (deployment engine) work starts; the user should provide them or confirm
  the `/mnt/data/techbey-apps*` layout at that point.
- No frontend yet (Phase 9).

## Important decisions

- ASP.NET Core / EF Core (Npgsql) chosen for the portal backend, matching
  the .NET application estate it manages.
- Permissions are embedded as JWT claims at login time (not re-queried per
  request), so a role/permission change takes effect on next login — acceptable
  for Phase 1; revisit if instant revocation becomes a requirement.
- `IAppDbContext` abstraction in Application (not full repository-per-entity)
  keeps Application testable via EF Core InMemory without leaking Npgsql
  specifics.

## Next phase

**Phase 2 — GitLab integration + repository/application configuration.**
Not started.
