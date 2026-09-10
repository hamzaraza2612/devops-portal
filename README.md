# DevOps Deployment & Application Management Platform

A commercial-ready internal platform for managing the full application lifecycle:
GitLab → build → container registry → DEV → QA → UAT → Production, with
approvals, rollback, RBAC, and audit at every step.

See `PROJECT_STATE.md` for current architecture, implemented features, and the
phase plan. Development proceeds in phases; only Phase 1 (core backend,
database, authentication, RBAC) is implemented so far.

## Quick start (local development)

```bash
dotnet build
dotnet test
```

Requires a local PostgreSQL instance and the following configuration (env vars
or `dotnet user-secrets`): `ConnectionStrings__Default`, `Jwt__SigningKey`
(32+ bytes). See `.env.example` for the full list used by Docker Compose.

## Running via Docker Compose

```bash
cp .env.example .env   # fill in POSTGRES_PASSWORD and JWT_SIGNING_KEY
docker compose up -d
```

The API is published on `${API_PORT:-8080}`. On first startup, if no users
exist yet, a bootstrap admin account is created; if `ADMIN_INITIAL_PASSWORD`
is left empty a random password is generated and printed once to the `api`
container logs — log in and change it immediately.

The PostgreSQL data volume (`devopsportal-postgres-data`) persists across
normal `docker compose restart` / `up` / `down`. It is only removed by
`docker compose down -v`, which should never be used for routine operations.
