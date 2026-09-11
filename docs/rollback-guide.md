# Rollback Guide

Reverting an environment to a previously-successful commit.

## When to use this

The current deployment to an environment is causing a problem, and you want
that environment back on a commit you know was good — without going
through a fresh promotion (which would need a new deployment from DEV
forward through every intermediate approval gate).

## How it works

On the application's page, each environment card has a **Rollback to**
dropdown, populated with that environment's own previously-**Succeeded**
deployments (never a Failed one — you can only roll back to something that
actually worked). Pick one, click **Rollback**.

This creates a **new** deployment record — it does not edit or "restore"
the old one — targeting the same commit, marked `isRollback: true` and
linked back to the deployment it's rolling back to. It goes through the
exact same execution path as a normal deploy: the same concurrency guard
(only one deployment in flight per environment), the same health check, the
same logging, the same audit trail. There is no separate, less-safe
"emergency rollback" code path.

**Rollback requires `deployments.rollback`** — a distinct permission from
the environment's normal deploy permission, so you can grant it narrowly
(e.g. to DEVOPS only) even to people who can otherwise deploy.

## What it does NOT do

- It does not touch any other environment — rolling back Production has no
  effect on QA/UAT/DEV.
- It does not revert your Git repository's branches or create any Git-side
  commit/revert — it only re-deploys a commit your infrastructure already
  ran successfully before. If you also need the branch itself walked back,
  that's a separate, manual Git operation outside the portal.
- It does not require going through a promotion/approval cycle — rollback
  is its own direct action, gated only by the `deployments.rollback`
  permission, precisely because an incident is the wrong moment to wait on
  an approval chain. Use it deliberately, and communicate the rollback to
  your team through whatever channel you normally use for incidents — the
  portal records *that* it happened (audit log, deployment history) but
  doesn't notify anyone beyond the normal deployment-outcome notification
  to whoever triggered it.

## After a rollback

Watch Deployment History the same way you would any other deployment —
Succeeded/Failed, with logs. If the rollback itself fails (e.g. the target
server is unreachable), you're in the same state as before: the previous
(problematic) deployment is still what's actually running, and you'll need
to address whatever's blocking execution — see the
[Troubleshooting Guide](troubleshooting-guide.md).

Once the environment is stable again, the normal forward-fix workflow is a
new commit going through DEV → promotion → approval → deploy like any
other change — a rollback isn't undone by "rolling forward" automatically;
it's just another deployment in the history.
