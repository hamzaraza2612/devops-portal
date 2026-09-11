# DevOps Deployment & Application Management Platform

A production-ready internal platform for managing the full application
lifecycle: Git commit → build → container registry → DEV → QA → UAT →
Production, with approvals, rollback, RBAC, multi-tenancy, secrets
management, and audit at every step.

Multi-tenant from the ground up (each customer/organization is a fully
isolated `Tenant`), built on .NET 8 (ASP.NET Core Web API, EF Core/Npgsql)
with a React 19 + TypeScript frontend, deployed via Docker Compose.

**Current release: v1.0.0** — see [`CHANGELOG.md`](CHANGELOG.md) for release
notes, and `PROJECT_STATE.md` for the complete phase-by-phase design
history and current architecture detail.

## Documentation

| Guide | For |
|---|---|
| [Installation Guide](docs/installation-guide.md) | Standing up a new instance: prerequisites, config, backup/restore, upgrades |
| [Configuration Guide](docs/configuration-guide.md) | Onboarding an organization: repositories, applications, targets, credentials |
| [Deployment Guide](docs/deployment-guide.md) | The DEV → QA → UAT → Production workflow, end to end |
| [Administrator Guide](docs/administrator-guide.md) | Users, roles, RBAC, tenants, audit |
| [Developer Guide](docs/developer-guide.md) | Local dev setup, extending the platform |
| [QA/UAT Guide](docs/qa-uat-guide.md) | Reviewing and approving promotion requests |
| [Production Approval Guide](docs/production-approval-guide.md) | The CTO approval gate |
| [Rollback Guide](docs/rollback-guide.md) | Reverting an environment to a known-good commit |
| [Troubleshooting Guide](docs/troubleshooting-guide.md) | Diagnosing a failed deployment or unhealthy component |
| [Architecture](docs/architecture.md) | System design, provider abstractions, current known gaps |

## Quick start (local development)

```bash
dotnet build
dotnet test
```

Requires a local PostgreSQL instance and the following configuration (env vars
or `dotnet user-secrets`): `ConnectionStrings__Default`, `Jwt__SigningKey`
and `Secrets__EncryptionKey` (32+ bytes each). See the
[Developer Guide](docs/developer-guide.md) for the full local setup,
including the frontend.

## Running via Docker Compose

```bash
cp .env.example .env   # fill in POSTGRES_PASSWORD, JWT_SIGNING_KEY, SECRET_ENCRYPTION_KEY
docker compose up -d
```

The frontend is published on `${FRONTEND_PORT:-8081}`, the API directly on
`${API_PORT:-8080}`. On first startup, if no users exist yet, a bootstrap
admin account is created; if `ADMIN_INITIAL_PASSWORD` is left empty a
random password is generated and printed once to the `api` container
logs — log in and change it immediately. See the full
[Installation Guide](docs/installation-guide.md) for everything else,
including backup/restore and upgrades.

The PostgreSQL data volume (`devopsportal-postgres-data`) persists across
normal `docker compose restart` / `up` / `down`. It is only removed by
`docker compose down -v`, which should never be used for routine operations.
