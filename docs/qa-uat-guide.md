# QA/UAT Reviewer Guide

For anyone holding a QA or UAT role, reviewing and approving promotion
requests into their environment.

## What you'll see

**Pending Requests** (sidebar) shows a tab per environment you have some
permission for — you'll typically see just your own tier (QA or UAT). Each
card shows:

- **Application** and the **commit** being promoted (short SHA — click
  through to the application page for the full commit message/author).
- **Who requested it** and **when**.
- **Current status** and a plain-language next step.

Nothing here has been deployed yet — a pending request only means someone
asked to promote a specific, already-DEV-successful (or already-QA-
successful, for a UAT request) commit into your environment.

## Reviewing

There's no built-in diff/changelog view — review happens against whatever
your team's process already is (a linked ticket, release notes, the commit
message itself, or checking the DEV/QA environment directly if it's
already deployed there and reachable). The portal's job is to track *who
approved what and when*, not to replace your team's review process.

## Approving or rejecting

**Approve** or **Reject** on the card. Both require permission
(`deployments.approve.qa` / `.uat`) — if you don't see these buttons, you
don't hold that permission for this environment; ask an admin.

**Approving does not deploy anything.** It only marks the request approved
and makes the (separate) **Deploy** button available to whoever has
`deployments.deploy.qa`/`.uat` — which may be you, or may be someone else
on your team, depending on how your organization split those permissions.

Rejecting requires no further action from you — the requester sees it was
rejected and can request again (typically after fixing whatever needed
fixing).

## After approval

If you also hold the deploy permission for this environment, click
**Deploy** on the same card once you're ready. Watch the deployment's
status on the same card or via Deployment History — it will show
Succeeded, Failed (with a reason), or still Running.

See the [Deployment Guide](deployment-guide.md) for the full pipeline this
fits into, and [Troubleshooting](troubleshooting-guide.md) if a deployment
you approved fails.
