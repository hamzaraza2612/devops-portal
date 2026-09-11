# Deployment Guide

How to move a commit from your Git repository through DEV → QA → UAT →
Production using the portal. Assumes your application is already
configured — see the [Configuration Guide](configuration-guide.md) if not.

## The workflow, end to end

```
GitLab commit
  │
  ▼
Deploy to DEV  ──────────────── explicit, direct, no approval gate
  │
  ▼
"Go Ahead to QA"  ─────────────  requests promotion (does NOT deploy)
  │
  ▼
QA approval  ──────────────────  a QA-permission holder approves the request
  │
  ▼
Deploy to QA  ──────────────────  separate, explicit action
  │
  ▼
"Go Ahead to UAT"  ─────────────  same pattern
  │
  ▼
UAT approval → Deploy to UAT
  │
  ▼
"Go Ahead to Production"  ──────  requests promotion; auto-creates a
  │                               CTO approval record
  ▼
CTO approval  ──────────────────  a deployments.approve.production holder
  │                               approves — this is a separate decision
  │                               from the promotion approval, though the
  │                               same permission gates both
  ▼
Deploy to Production  ──────────  explicit, separate action; blocked until
                                   BOTH the promotion and the CTO approval
                                   are granted
  │
  ▼
Health check → Deployment history entry → Audit log entry
```

**Every arrow above is a separate, explicit action.** Nothing in this
system ever deploys automatically because a previous step succeeded —
"Go Ahead to QA" only records a request; it never touches a running
container. This is enforced server-side, not just hidden in the UI: even
if you script around the UI and call the API directly, approving a
promotion will not create a deployment, and deploying to Production without
CTO approval returns an error naming exactly what's missing.

## Deploying to DEV

Application page → DEV card → optionally "Look up latest available commit"
(a live call to your Git provider) → enter/confirm the commit SHA → **Deploy
to DEV**. This is the only environment with no approval gate — anyone
holding `deployments.deploy.dev` can deploy any commit directly.

## Promoting to QA / UAT / Production

From the application's page, click **"Go Ahead to {environment}"** on the
environment you're promoting *from* a successful deployment *into*. This
creates a `PromotionRequest`, snapshots the source commit, and — if a
branch is configured for both the source and target environments — attempts
a git-level branch merge/promotion (via a merge request on GitLab). **A
failed branch-level merge never blocks the promotion request itself** — you
still see a normal pending request to approve, with the git-side failure
recorded and shown on the card so you know to resolve it directly in
GitLab.

## Approving

Pending Requests (sidebar), grouped by target environment as a tab per
tier. Only tabs you hold a relevant permission for are shown at all. Each
card shows application, commit, requester, request time, current status,
and a plain-language next-action line. **Approve** and **Reject** are
separate buttons from **Deploy** — approving never deploys.

## Deploying an approved promotion

Once approved (and, for Production, once CTO approval is also granted),
the **Deploy** button on the same card becomes active. Click it. This is a
distinct API call from approval — the deployment doesn't exist until this
click happens.

## What happens during a deployment

The background worker picks up the queued job, re-validates the
target/config server-side (configuration could have changed since the
request was made), runs `docker compose down` then `up -d` in the
configured directory on this VM (see
[Architecture](architecture.md#deployment-execution-topology)), then runs
the configured health check (if any). The deployment is marked
**Succeeded** only if compose exits cleanly *and* the health check passes
(or is skipped by config) — never optimistically. Every step is logged,
sanitized, to that deployment's log viewer (Deployment Details page).

**Only one deployment can be in flight per application+environment at a
time** — this is enforced at the database level, not just in the UI, so it
holds even under two people clicking Deploy at the same instant.

## Retrying a failed deployment

An approved promotion whose deployment attempt **failed** can be
re-deployed by clicking **Deploy** again on the same card — the portal
does not require you to create a new promotion request just because the
last attempt failed. (A promotion whose deployment **succeeded** cannot be
re-deployed the same way — a completed pipeline stage is done; deploy a
new commit through a new promotion, or see the
[Rollback Guide](rollback-guide.md) if you specifically need to go back to
a previous commit.)

## Deployment history and logs

Deployment History (sidebar) — filter by application, environment, status,
date range. Click any row for full detail: commit, branch, timestamps,
duration, health-check result, failure reason if any, and the complete
sanitized log. Logs auto-refresh every 4 seconds while a deployment is
active and stop automatically once it completes.

## Audit trail

Every request, approval, rejection, deploy, and rollback is recorded in
the audit log (Admin → Audit, for holders of `audit.view`) with actor,
timestamp, and outcome — this is not optional or configurable off.

## Next steps

- [QA/UAT Guide](qa-uat-guide.md) — what a QA/UAT reviewer specifically does.
- [Production Approval Guide](production-approval-guide.md) — the CTO
  approval flow in detail.
- [Rollback Guide](rollback-guide.md).
- [Troubleshooting Guide](troubleshooting-guide.md) — a specific deployment
  failed, what now.
