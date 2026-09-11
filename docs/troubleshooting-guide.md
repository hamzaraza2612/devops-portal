# Troubleshooting Guide

Common operational issues and how to diagnose them. For installation-
specific problems (the portal itself won't start), see the
[Installation Guide's own Troubleshooting section](installation-guide.md#troubleshooting).

## A deployment failed

Open the deployment in **Deployment History** — the **Failure reason** and
full **log** panel tell you exactly what happened; nothing is hidden or
summarized away. Common causes, in order of likelihood:

- **`docker compose up failed (exit code N)` / "failed to connect to the
  docker API"** — the VM the portal's API process runs on either doesn't
  have Docker running, or the compose file/target directory has a real
  problem (bad image reference, port conflict, missing `.env` file the
  compose file expects). Check `docker compose ps`/`docker compose logs`
  directly on that VM for the underlying Docker-level error — the portal's
  log is exactly what Docker reported, not a reinterpretation of it.
- **`Health check failed: ...`** — compose itself succeeded, but the
  configured health check (HTTP GET or TCP connect) never returned healthy
  within `healthCheckTimeoutSeconds`. The application container may have
  started but isn't actually serving yet (slow startup — consider raising
  the timeout) or is crash-looping (check `docker logs <container>` on the
  target host) or the configured `healthCheckEndpoint` is simply wrong.
- **`Deployment timed out after N minute(s)`** — the whole deployment
  (compose commands + health check combined) exceeded
  `Deployment:ExecutionTimeoutMinutes` (default 20). Usually means Docker
  itself hung (e.g. an image pull that's stuck) rather than a normal
  failure — check the target host directly.
- **`DeploymentRootPath is no longer under an allowed deployment root`** —
  someone changed the target server's allowed-roots configuration after
  this environment was set up. Fix the `ApplicationEnvironment` config or
  the target server's allowed roots (see the
  [Configuration Guide](configuration-guide.md)).
- **`Failed to resolve secret 'X'`** — a credential this deployment needs
  (see Credentials in the Configuration Guide) is missing, inactive, or the
  encryption key changed since it was stored. Check Admin → Credentials for
  that application/environment.

## A deployment is stuck "Running" and never completes

Check `/health/worker` — if it reports `Unhealthy`, the background worker
process has stopped (see below); restart the API container. If the worker
is healthy but a deployment still appears stuck, it's very likely actually
mid-execution against a genuinely slow/hung `docker compose` call on the
target — wait for `Deployment:ExecutionTimeoutMinutes` to elapse (it will
resolve itself to Failed with a clear timeout reason) rather than
restarting the API mid-deployment, which would leave the row `Running`
forever with no process left to finish it.

## "A different deployment is already in progress for this application and
environment" (HTTP 409)

Working as intended — only one deployment can run per application+
environment at a time. Wait for the in-flight one to finish (Deployment
History shows it), then retry.

## Can't approve/deploy/promote — HTTP 403 "Missing required permission"

The message names the exact permission missing. Ask a tenant admin to
grant it via Admin → Roles, either to your existing role or a new custom
one — see the [Administrator Guide](administrator-guide.md).

## Health endpoint reports Unhealthy or Degraded

`GET /health` breaks down by component:

- **`database: Unhealthy`** — Postgres is unreachable or down. Check
  `docker compose ps postgres` and its logs.
- **`worker: Unhealthy`** — the background deployment-job worker isn't
  running inside the API process. This should be very rare (it only stops
  on an unhandled fault in the worker's own dequeue loop, not on a job
  failure — see [Architecture](architecture.md)); restart the `api`
  container if you see this.
- **`integrations: Degraded`** — SMTP and/or a real remote-execution
  mechanism isn't configured. **This is expected and does not indicate a
  problem** unless you specifically expected email notifications to be
  working — see the [Configuration Guide](configuration-guide.md#8-notification-provider).
  It never blocks `/health/ready` or normal operation.

## Notifications aren't being sent

Check `SMTP_HOST` is actually set (Configuration Guide, section 8) — an
unconfigured SMTP is a silent, deliberate no-op (a warning is logged, not
an error), not a bug. If SMTP is configured and still not sending, check
the API container logs for the specific SMTP error (auth failure, TLS
issue, etc.) — the portal logs the underlying send failure but never
retries indefinitely.

## Branch promotion shows "failed" on a promotion card

This means the Git-level branch merge (GitLab merge request) didn't
succeed — usually a missing/insufficient `accessTokenEnvVarName` token
scope, or a genuine merge conflict. **This never blocks the promotion
itself** — the promotion request, approval, and deployment all still work
normally; only the git-branch-level side effect failed. Resolve the
conflict directly in GitLab if needed; the deployment always promotes the
exact commit SHA recorded at request time, regardless of the branch
merge's outcome.

## "Container status: unreachable" / container monitoring shows nothing

Expected today for every target server — there is no real remote Docker
connectivity mechanism implemented yet (see
[Architecture: Known gaps](architecture.md#known-gaps)). This is not a
configuration mistake on your part; it's a documented, current platform
limitation.

## Something else

Check the API container's logs (`docker compose logs api`) — exceptions
are logged server-side with full detail even when the client only sees a
generic message (the API deliberately never returns a stack trace to a
caller). If you're still stuck, gather: the exact error message shown in
the UI, the relevant deployment/promotion ID, and the API log around that
timestamp, before escalating to whoever maintains your installation.
