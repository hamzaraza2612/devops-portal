# Administrator Guide

Managing users, environment access, and authorization.

## Authorization model

This is a single-organization platform — there is no tenant/organization
concept to administer. Every user's access is defined by three things on
their own `User` record:

- **`isAdmin`** — full access to everything: every application,
  environment, secret, and every admin-only area (Users, Repositories,
  Target Servers, Build Servers, Audit log). There is no partial-admin
  tier; an admin cannot be scoped to "some" environments.
- **`canApproveProduction`** — an independent flag, deliberately separate
  from environment access, that lets a user (a "CTO"-type approver) grant
  the required CTO approval on a Production promotion request. A user can
  hold this flag without having Production deployment access at all, and
  a user with Production deployment access does not get this flag for
  free — approving and deploying Production are always two different
  people/permissions unless someone deliberately holds both.
- **Environment access** — which of DEV/QA/UAT/PRODUCTION a user may act
  on. Access to a given environment grants that environment's full
  deploy/promote/approve/deploy-to-it set of actions together (e.g. QA
  access lets a user both request a promotion into QA and approve/deploy
  it — there is no separate "can request but not approve" tier for a
  single environment). Holding access to *any* environment also grants a
  base tier of read/operate permissions that apply across every
  application: viewing applications and deployments, viewing/controlling/
  recreating containers, viewing/requesting builds, and viewing/revealing
  secrets — each of those is then further narrowed to the *specific*
  environment being acted on, enforced server-side (a user with only QA
  access gets a 403 calling a Production-specific endpoint, even though
  they hold the base "containers.view" permission).

Every permission check server-side is still driven by the same fixed
permission-code strings as before (e.g. `deployments.deploy.dev`,
`containers.control`, `secrets.reveal`) — what changed is only what
grants them; there is no role catalog to create, edit, or assign anymore.

## Users

Admin → Users. `POST /api/users` to create:

```json
{
  "username": "jsmith",
  "email": "jsmith@example.com",
  "fullName": "Jane Smith",
  "password": "...",
  "isAdmin": false,
  "canApproveProduction": false,
  "environmentDefinitionIds": ["<DEV environment id>", "<QA environment id>"]
}
```

Deactivating a user (rather than deleting) is the only removal path — this
preserves their attribution on historical deployments/audit entries.
Password minimum is 8 characters; an admin can reset any user's password,
and a user can change their own (requires their current password).

To change what a user can do, update their `isAdmin`/`canApproveProduction`
flags and/or their environment access list via `PUT /api/users/{id}` or the
Admin → Users screen — there's no separate role-assignment step.

**Access changes take effect on next login**, not instantly — a signed-in
user's token already carries their permission set. Revoking access
immediately requires deactivating the user, not just changing their
environment access or flags.

## Repositories, deployment targets, build servers

See the [Configuration Guide](configuration-guide.md) — these are
administrative configuration, all reachable from the Admin section of the
sidebar. Repositories and Target Servers each have a "Test Connection"
action (admin-only) to verify GitLab/SSH connectivity is actually
configured correctly before relying on it for a real deployment — see the
Configuration Guide for details.

## Audit log

Admin → Audit (`audit.view`). Every security-sensitive action — logins
(success and failure), every deployment/promotion/approval/rollback
decision, every credential create/update/delete/reveal, every user/
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
