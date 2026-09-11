# Production Approval Guide

For whoever holds `deployments.approve.production` (the CTO role by
default) — the final gate before anything reaches Production.

## Why this exists

Production deployment in this platform always requires two separate,
explicit decisions before it can happen, on top of the same promotion/
approval pattern every other environment uses:

1. The promotion request into Production is approved (same as QA/UAT).
2. A **CTO approval** record — created automatically the moment the
   promotion is requested — is separately granted.

Only once **both** are true does the **Deploy to Production** button
become active for whoever holds `deployments.deploy.production`. Note:
`deployments.approve.production` (deciding both of the above) is
deliberately **not** the same permission as `deployments.deploy.production`
(actually deploying) — by default, the CTO role holds only the former.
Approving and deploying to Production are two different actions, and can
be two different people, even though one call today decides both the
promotion and the linked CTO approval when you hold the approval
permission.

## What you'll receive

If SMTP is configured (see the [Configuration Guide](configuration-guide.md)),
you'll get an email the moment a Production promotion is requested, with a
secure, read-only preview link — no login required to *see* what's being
requested (application, commit, requester, timestamp), but the link cannot
itself approve or reject anything. That link expires after 7 days; the
underlying request doesn't expire, only the email's preview link does.

If SMTP isn't configured, check **Pending Requests → PRODUCTION** in the
portal directly — nothing about the approval flow depends on email working.

## Deciding

Log in, go to **Pending Requests → PRODUCTION**. The card shows the same
information as the email preview, plus (once you're authenticated) the
actual **Approve**/**Reject** buttons. Deciding here is the *only* way to
actually approve or reject — there is no "click this link to approve" path
anywhere, by design, so an intercepted or forwarded email can never be used
to approve a production deployment on its own.

Approving here also approves the underlying promotion request in the same
action (they're gated by the same permission) — you'll see the card update
to show CTO approval granted.

## After approval

Whoever holds `deployments.deploy.production` (again, not necessarily you)
clicks **Deploy**. You can verify the outcome afterward via Deployment
History, or the same Pending Requests card while it's still visible.

## Rejecting

Rejecting stops the promotion — no deployment can happen from it. The
requester will need to request again if they still want to promote (after
addressing whatever the rejection was about, communicated outside the
portal — there's a free-text notes field on the decision, but no built-in
discussion thread).

See the [Deployment Guide](deployment-guide.md) for the full pipeline, and
the [Rollback Guide](rollback-guide.md) if a Production deployment needs to
be walked back after the fact.
