# Configuration Guide

Everything below is configured through the API/Admin UI or environment
variables — **none of it requires touching source code**. This guide covers
how this single-organization instance gets from an empty install to a
working deployment pipeline.

If you haven't installed the portal yet, see the
[Installation Guide](installation-guide.md) first.

## 1. Log in as the bootstrap admin

There's no tenant or organization to create — the portal is
single-organization. Log in as the bootstrap admin account created during
installation (see the [Installation Guide](installation-guide.md)) and
start creating users, repositories, and applications directly; the four
standard environment tiers (DEV/QA/UAT/PRODUCTION) already exist as fixed
platform reference data. See the [Administrator Guide](administrator-guide.md)
for how to grant other users admin access, Production-approval ("CTO")
rights, or access to specific environments.

## 2. Git provider

GitLab is the only implemented Git provider today (`RepositoryProvider`
enum has room for others; `IGitProviderClient` is provider-abstracted, but
only `GitLabProviderClient` exists). See the step-by-step
[GitLab integration walkthrough](#gitlab-repository-walkthrough) below.

## 3. Repositories

Admin → Repositories → New Repository, or `POST /api/repositories`:

```json
{
  "name": "MyApp",
  "url": "https://gitlab.example.com/mygroup/myapp",
  "provider": 0,
  "description": null,
  "accessTokenEnvVarName": "GITLAB_TOKEN_MYAPP"
}
```

`accessTokenEnvVarName` names an environment variable — **never a token
value** — that must be set on the `api` container's environment (see
`docker-compose.yml`'s `environment:` block for the `api` service) if the
project is private or you want branch-promotion (merge) support. Leave it
unset for a public project; commit lookups still work unauthenticated.

### GitLab repository walkthrough

1. In GitLab: **Project → Settings → Access Tokens**, create a token
   scoped `read_api` (add `write_repository`/`api` too if you want the
   portal's "Go Ahead to QA/UAT" branch-promotion feature to actually merge
   branches, not just record that it couldn't).
2. Add it to the `api` container's environment under whatever name you'll
   reference in step 3 above (e.g. `GITLAB_TOKEN_MYAPP=glpat-xxxx` in
   `.env`, wired through `docker-compose.yml`).
3. Create the `Repository` row (above), setting `accessTokenEnvVarName` to
   that same name.
4. Verify: `GET /api/applications/{id}/environments/{envId}/commits/latest`
   (once you've created the application below) should return a real commit
   — confirms the URL and token are both correct.

## 4. Applications

There is currently no "create application" form in the UI — use the API
directly (`applications.manage` permission):

```
POST /api/applications
{
  "name": "MyApp",
  "slug": "myapp",
  "description": null,
  "deploymentMode": 0,
  "repositoryId": "<repository id from step 3>",
  "sourcePath": null
}
```

`deploymentMode: 0` (`LegacyFilesystem`) is the only mode that actually
executes deployments today — see
[Architecture: Deployment execution topology](architecture.md#deployment-execution-topology).
`sourcePath` is only needed for a monorepo (the subdirectory this
application lives in).

## 5. Environments and deployment targets

The platform has the four standard tiers (DEV/QA/UAT/PRODUCTION) —
`GET /api/environments` lists them; these are fixed platform reference
data, not something you add/remove.

**Deployment Targets** (Admin → Deployment Targets) are the hosts your
applications actually deploy to:

```
POST /api/target-servers
{ "name": "prod-host-1", "description": "...", "hostname": "10.0.1.5" }
```

Then declare which filesystem paths on that host are allowed deployment
roots — this is a real, enforced security boundary
(`ApplicationEnvironment.DeploymentRootPath` must fall under one of these,
or the deploy is rejected server-side, not just hidden in the UI):

```
POST /api/target-servers/{id}/allowed-roots
{ "rootPath": "/opt/apps" }
```

Finally, wire up each environment tier your application will actually use:

```
PUT /api/applications/{appId}/environments/{environmentDefinitionId}
{
  "targetServerId": "<id from above>",
  "branchName": "main",
  "deploymentRootPath": "/opt/apps/myapp",
  "publishSubPath": "publish",
  "backupSubPath": "Backups",
  "composeFilePath": "docker-compose.yml",
  "useDownWithVolumesOnDeploy": false,
  "healthCheckType": 0,
  "healthCheckIntervalSeconds": 30,
  "healthCheckTimeoutSeconds": 10,
  "isActive": true
}
```

`branchName` is what drives the "Go Ahead to QA/UAT/Production" git
branch-promotion feature — set a different branch per tier if your
workflow uses one (e.g. `develop` for DEV, `main` for everything above it),
or the same branch everywhere if you deploy by commit SHA regardless of
branch.

`healthCheckType: 0` is `None` — the deployment is considered successful
once `docker compose up` exits cleanly. Set it to an HTTP or TCP check
(`healthCheckEndpoint` required) if your application exposes one; see
[Architecture](architecture.md) for exactly how this gates deployment
success.

## 6. Registry

Only relevant if you build container images from source (see "Build
provider" below). `BuildConfiguration.ImageRegistry`/`ImageName` are set
per application via `PUT /api/applications/{id}/build-configuration`
alongside the build-server wiring:

```json
{
  "buildServerId": "<id>",
  "jobName": "myapp-build",
  "imageRegistry": "registry.example.com/mygroup",
  "imageName": "myapp",
  "imageTagStrategy": 0
}
```

`imageTagStrategy: 0` is `CommitSha` (recommended — always traceable, never
`latest`); `1` is `BuildNumber`; `2` is `SemVer` (requires a version string
at build-request time).

**Note**: building an image via this path produces a `Release` row, but
nothing in the portal deploys from a `Release` yet — see
[Architecture](architecture.md#known-gaps) for the current state of that
gap. Every application that actually deploys today uses the Legacy
(prebuilt publish directory) path.

## 7. Build provider

Jenkins is the only implemented build provider today. Admin → Build
Servers → New Build Server, or `POST /api/build-servers`:

```json
{
  "name": "main-jenkins",
  "providerType": 0,
  "baseUrl": "https://jenkins.example.com",
  "username": "portal-svc",
  "apiTokenEnvVarName": "JENKINS_TOKEN_MAIN"
}
```

Same env-var-name-only credential pattern as repositories — set
`JENKINS_TOKEN_MAIN` on the `api` container's environment, never a token
value in this request.

## 8. Notification provider

Email is the only implemented notification channel, and it's configured
entirely via environment variables — there's no database-backed
"notification provider" object to create:

```
SMTP_HOST=smtp.example.com
SMTP_PORT=587
SMTP_ENABLE_SSL=true
SMTP_USERNAME=notifications@example.com
SMTP_PASSWORD=<app password or SMTP credential>
SMTP_FROM_ADDRESS=notifications@example.com
SMTP_FROM_NAME=DevOps Portal
```

Leave `SMTP_HOST` empty and the portal stays fully functional — approval
requests and deployment outcomes just aren't emailed (a warning is logged
instead). Nothing here is ever required for the portal to start or for the
deployment workflow to function.

## 9. URLs

- **Portal's own public URL** (used to build clickable links in
  notification emails): `PORTAL_BASE_URL` env var. Optional — leave empty
  and emails send without a link.
- **Per-environment application URL** (shown as a link on the application's
  details page, e.g. `https://myapp-qa.example.com`): set via
  `applicationUrl` on the same `PUT .../environments/{id}` call from step 5.

## 10. Credentials/secrets

Two related but distinct things:

- **Repository/build-server access tokens** — see steps 3 and 7 above:
  always an environment-variable *name* on the `Repository`/`BuildServer`
  row, the actual value only ever lives as a real environment variable on
  the `api` container.
- **Application/database credentials your team needs day-to-day**
  (database passwords, API keys other services need) — stored encrypted
  (AES-256-GCM) via the Credentials page (Admin nav → Credentials, or
  `POST /api/secrets`), scoped to a specific application+environment,
  application-wide, or global. Values are never returned by any API
  response except the explicit, separately-permissioned "reveal" action
  (`secrets.reveal`, distinct from `secrets.view` which shows only
  metadata) — see the [Administrator Guide](administrator-guide.md) for
  the permission model.

---

At this point you have a fully configured application. See the
[Deployment Guide](deployment-guide.md) to actually deploy it.
