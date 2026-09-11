# Installation Guide

Installing the DevOps Deployment Platform on a fresh VM using Docker Compose.
This is the only supported production installation path today — there is no
Kubernetes/Helm chart or bare-metal installer.

## 1. Prerequisites

- A Linux VM (or equivalent) with **Docker Engine 24+** and the **Docker
  Compose plugin** (`docker compose`, not the standalone `docker-compose`
  v1) installed.
- Outbound network access from this VM to:
  - Your Git provider (GitLab today — see [Configuration Guide](configuration-guide.md)).
  - Your build server, if you use one (Jenkins today).
  - An SMTP relay, if you want deployment/approval email notifications.
  - Every `TargetServer` you configure (see below) — this VM is where
    deployments actually execute (`docker compose` runs locally on it; see
    [Architecture](architecture.md) for what that does and doesn't mean).
- Nothing else. There is no separate database server to provision — Postgres
  runs as one of the Compose services and keeps its data in a named Docker
  volume on this same VM.

**Important, read before you deploy anything**: the portal's own
`docker compose down`/`up` calls for an application's deployment run on
**this VM**, not on some other "target server" reached over the network —
see the [Architecture Guide](architecture.md#deployment-execution-topology)
for the current, honest state of that gap before you plan a multi-server
rollout.

## 2. Get the code

```bash
git clone <your-repository-url> devops-portal
cd devops-portal
```

## 3. Configure environment variables

```bash
cp .env.example .env
```

Edit `.env`. At minimum, you must set:

| Variable | Required | Notes |
|---|---|---|
| `POSTGRES_PASSWORD` | **Yes** | `docker compose config` refuses to render without it. Use a strong, generated password. |
| `JWT_SIGNING_KEY` | **Yes** | 32+ bytes. Generate with `openssl rand -base64 48`. Every login token is signed with this — losing it invalidates all sessions; changing it invalidates all existing tokens (harmless, users just log in again). |
| `SECRET_ENCRYPTION_KEY` | **Yes** | 32+ bytes. Generate with `openssl rand -base64 48`. Encrypts every stored credential/secret value (AES-256-GCM). **There is no key-rotation tool** — write this down somewhere safe (a password manager, not a file in this repo). Losing it, or changing it after secrets exist, makes every stored secret permanently unreadable. |

Everything else in `.env.example` has a safe default or is optional (SMTP,
the portal's own public base URL, the admin account's initial username/email).
Read the comments in `.env.example` — they explain what each variable does
and what happens if you leave it unset (nothing here is silently insecure;
an unset optional value just means that specific feature — email
notifications, a clickable link in emails — doesn't do anything until you
configure it).

Leave `ADMIN_INITIAL_PASSWORD` empty to have a random password generated for
you on first startup (see step 5).

## 4. Start the stack

```bash
docker compose up -d
```

This builds the `api` and `frontend` images locally (no registry push/pull
needed) and starts three containers: `devopsportal-postgres`,
`devopsportal-api`, `devopsportal-frontend`. Database migrations run
**automatically** on API startup — there is no separate migration step to
run by hand on a fresh install.

Watch it come up:

```bash
docker compose logs -f api
```

You should see EF Core applying migrations, then a bootstrap admin account
being created (see below), then `Now listening on: http://+:8080`.

## 5. First login

- URL: `http://<your-vm>:${FRONTEND_PORT:-8081}/login`
- Username: whatever you set `ADMIN_USERNAME` to (default `admin`).
- Password: whatever you set `ADMIN_INITIAL_PASSWORD` to — or, if you left
  it empty, check the API logs for a one-time generated password:

  ```bash
  docker compose logs api | grep -A1 "Bootstrap"
  ```

**Change this password immediately after first login** (Users → your
account, or ask another admin to reset it) — it may have transited your
shell history or a log aggregator depending on how you set it.

This bootstrap account is a full administrator (`isAdmin = true`,
`canApproveProduction = true`) — it can do everything, including creating
every other user. See the [Administrator Guide](administrator-guide.md)
for the full authorization model before creating additional users.

## 6. Verify the install

```bash
curl http://localhost:${API_PORT:-8080}/health
```

Should return `{"status":"Healthy", ...}` with every component (`database`,
`worker`, `integrations`) listed. See
[Troubleshooting](troubleshooting-guide.md#health-endpoint-reports-unhealthy-or-degraded)
if anything reports `Unhealthy`. `integrations` reporting `Degraded` is
normal and expected until you configure SMTP/build servers — it never
blocks startup.

## 7. Next steps

Configure your first Git repository, target server (including its SSH
connection), and application — see the
[Configuration Guide](configuration-guide.md).

---

## Upgrading

1. `git pull` (or pull your new image/tag).
2. **Back up the database first** — see [Backup](#backup) below. An upgrade
   that includes a migration is the single highest-risk moment for existing
   data.
3. `docker compose up -d --build`

Database migrations run automatically on the new API container's startup,
same as a fresh install — there is no manual migration command to run.
Migrations in this codebase are additive-only by design (new nullable
columns/tables, never a destructive rename or drop of existing data) — see
`PROJECT_STATE.md`'s per-phase "Database changes" sections for the full
history if you want to review exactly what each one does before upgrading
across many versions at once.

If a migration ever fails partway (should not happen with additive
migrations against a healthy database, but if it does): the API container
will fail to start and log the EF Core error. Do not attempt to hand-edit
the `__EFMigrationsHistory` table. Restore your pre-upgrade backup and get
in touch with whoever maintains your deployment before retrying.

## Backup

A daily backup is the minimum acceptable posture for a production instance —
this platform is your deployment history, audit trail, and (encrypted)
credential store; losing it is a real incident, not just an inconvenience.

```bash
# Run from the VM (or anywhere with network access to the postgres container/port):
POSTGRES_PASSWORD=<your password> \
PGHOST=localhost PGPORT=5432 PGUSER=devopsportal PGDATABASE=devopsportal PGPASSWORD=<your password> \
BACKUP_DIR=/var/backups/devopsportal \
  ./scripts/backup-database.sh
```

Schedule it (cron shown; use a systemd timer if you prefer):

```cron
0 2 * * * PGHOST=localhost PGUSER=devopsportal PGDATABASE=devopsportal PGPASSWORD=*** BACKUP_DIR=/var/backups/devopsportal /path/to/devops-portal/scripts/backup-database.sh >> /var/log/devopsportal-backup.log 2>&1
```

Default retention inside the script is 14 daily backups in `BACKUP_DIR` —
pair this with whatever longer-term/offsite copy policy your infrastructure
already uses for other stateful services (this script only manages its own
local directory).

**Back up before every upgrade**, not just on a schedule — see step 2 above.

## Restore

```bash
docker compose stop api frontend   # keep postgres running, stop what talks to it
PGHOST=localhost PGUSER=devopsportal PGDATABASE=devopsportal PGPASSWORD=<your password> \
  ./scripts/restore-database.sh /var/backups/devopsportal/devopsportal-devopsportal-<timestamp>.dump --yes
docker compose start api frontend
```

**Verify a backup is actually restorable before you need it in an
incident** — restore it into a throwaway database (a different
`PGDATABASE` name) periodically, confirm the API starts against it cleanly
and `dotnet ef migrations has-pending-model-changes` reports none, then
drop the throwaway database. See the comment block at the top of
`scripts/restore-database.sh` for the exact procedure. A backup file that
exists on disk is not the same thing as a backup you know works.

## Troubleshooting

See the dedicated [Troubleshooting Guide](troubleshooting-guide.md) for
day-to-day operational issues (a specific deployment failing, a health
check reporting unhealthy, etc.). The issues below are specifically about
getting the portal itself installed and running.

**`docker compose config` fails with "required variable ... is missing a
value"** — you haven't set `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`, or
`SECRET_ENCRYPTION_KEY` in `.env`. This is deliberate: the portal refuses
to start with a missing or default secret rather than silently running
insecurely.

**API container restarts in a loop / logs
`Jwt:SigningKey ... must be configured with at least 32 bytes`** — your
`JWT_SIGNING_KEY` or `SECRET_ENCRYPTION_KEY` is set but too short. Both must
be at least 32 bytes; `openssl rand -base64 48` comfortably clears that.

**Can't reach the UI at all** — confirm `docker compose ps` shows all three
containers `Up`/`healthy`, and that `${FRONTEND_PORT:-8081}` isn't blocked
by a firewall/security group on the VM.

**Lost the database entirely and have no backup** — there is nothing this
guide can do for you. This is exactly the scenario the Backup section above
exists to prevent; set it up now for next time.
