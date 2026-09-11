# Administrator Guide

Managing users, roles, tenants, and the RBAC model.

## Platform vs. tenant administration

Two distinct admin levels exist:

- **Platform administrator** (`PLATFORM_ADMIN` role, `tenants.manage`
  permission) — created once at installation (see the
  [Installation Guide](installation-guide.md)). Can create/deactivate
  Tenants. Holds **no** application, deployment, or credential permissions
  inside any tenant — a platform admin cannot see a tenant's applications,
  secrets, or deployment history. This separation is deliberate: the entity
  that provisions organizations is not automatically able to read their
  data.
- **Tenant administrator** (`ADMIN` role within a tenant, created
  automatically when the tenant is provisioned) — full access within that
  one tenant only. Everything in the rest of this guide describes
  tenant-level administration.

Every tenant is fully isolated: a user in one tenant can never see another
tenant's applications, users, deployments, secrets, or audit log, regardless
of role — enforced server-side on every query, not just hidden in the UI.

## Users

Admin → Users. `POST /api/users` to create:

```json
{
  "username": "jsmith",
  "email": "jsmith@example.com",
  "fullName": "Jane Smith",
  "password": "...",
  "roleIds": ["<role id>"]
}
```

A user can hold multiple roles; their effective permissions are the union.
Deactivating a user (rather than deleting) is the only removal path — this
preserves their attribution on historical deployments/audit entries.
Password minimum is 8 characters; an admin can reset any user's password,
and a user can change their own (requires their current password).

## Roles and permissions

Six default roles are provisioned with every new tenant — **DEVELOPER**,
**QA**, **UAT**, **DEVOPS**, **CTO**, **ADMIN** — each with a sensible
starting permission set (see the table below). You can also create
**custom roles** with any combination of permissions (Admin → Roles → New
Role, or `POST /api/roles`) — the default roles are a starting point, not a
fixed list.

| Role | Typical permissions |
|---|---|
| DEVELOPER | Deploy to DEV, promote to QA, view builds/deployments, view+reveal credentials |
| QA | Approve + deploy QA promotions |
| UAT | Approve + deploy UAT promotions |
| DEVOPS | Deploy to every environment, promote everywhere, approve QA/UAT, rollback, manage secrets/build servers/target servers |
| CTO | Approve Production promotions only — **not** deploy to Production; approving and deploying Production are always two different actions, even if the same person holds both permissions |
| ADMIN | Everything, including user/role/repository/tenant-scoped management |

Permissions are granular (e.g. `deployments.approve.qa` is separate from
`deployments.approve.uat`/`.production`) so you can grant exactly the
access a role needs — see Admin → Roles for the full permission catalog
when creating a custom role.

**Permission changes take effect on next login**, not instantly — a
signed-in user's token already carries their permission set. Revoking
access immediately requires deactivating the user, not just changing their
role.

## Repositories, deployment targets, build servers

See the [Configuration Guide](configuration-guide.md) — these are
administrative, tenant-scoped configuration, all reachable from the Admin
section of the sidebar.

## Audit log

Admin → Audit (`audit.view`). Every security-sensitive action — logins
(success and failure), every deployment/promotion/approval/rollback
decision, every credential create/update/delete/reveal, every user/role/
repository/target-server change — is recorded with actor, timestamp, and
outcome. Filter by user, action, and date range. This is not configurable
off and has no retention-deletion tooling built in (see
[Architecture](architecture.md#known-gaps)) — plan your database backup
retention accordingly if you need long-term audit retention.

## Health monitoring

`GET /health` (and the more granular `/health/live`, `/health/ready`,
`/health/db`, `/health/worker`, `/health/integrations`) — unauthenticated,
safe to point an uptime monitor or load balancer health check at. See the
[Troubleshooting Guide](troubleshooting-guide.md#health-endpoint-reports-unhealthy-or-degraded)
for what each component means.

## Backup and recovery

See the [Installation Guide's Backup/Restore sections](installation-guide.md#backup) —
this is an infrastructure-level responsibility, not something managed from
within the application itself.
