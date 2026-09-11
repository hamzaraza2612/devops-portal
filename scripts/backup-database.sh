#!/usr/bin/env bash
# Backs up the portal's Postgres database to a timestamped, compressed
# pg_dump custom-format archive, verifies the archive is structurally
# readable, and prunes backups older than the configured retention window.
#
# Deliberately infra-agnostic: every location/credential comes from an
# environment variable (standard libpq PG* vars plus BACKUP_DIR/
# BACKUP_RETENTION_DAYS below) — nothing here assumes a specific host,
# container name, or filesystem layout. Point it at the portal's database
# however it's reached in your environment (same host, docker exec wrapper,
# a managed Postgres endpoint, etc.).
#
# Usage:
#   PGHOST=localhost PGPORT=5432 PGUSER=devopsportal PGDATABASE=devopsportal \
#   PGPASSWORD=*** BACKUP_DIR=/var/backups/devopsportal \
#     ./scripts/backup-database.sh
#
# Suggested schedule: once daily via cron/systemd timer, e.g.
#   0 2 * * * BACKUP_DIR=/var/backups/devopsportal ... /path/to/backup-database.sh
# Recommended retention: 14 daily backups (BACKUP_RETENTION_DAYS, below) plus
# whatever longer-term/offsite copy policy your infrastructure already uses
# for other stateful services — this script only manages its own local
# directory's retention, it does not replace an offsite/3-2-1 backup policy.

set -euo pipefail

: "${PGDATABASE:?PGDATABASE is required}"
: "${PGUSER:?PGUSER is required}"
BACKUP_DIR="${BACKUP_DIR:-./backups}"
BACKUP_RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-14}"

mkdir -p "$BACKUP_DIR"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_file="${BACKUP_DIR}/devopsportal-${PGDATABASE}-${timestamp}.dump"

echo "Backing up database '${PGDATABASE}' to ${backup_file} ..."
pg_dump --format=custom --no-owner --no-privileges --file="$backup_file"

echo "Verifying archive integrity (pg_restore --list, no data touched) ..."
if ! pg_restore --list "$backup_file" > /dev/null; then
  echo "ERROR: backup verification failed — ${backup_file} is not a readable pg_dump archive." >&2
  rm -f "$backup_file"
  exit 1
fi

echo "Backup verified: $backup_file ($(du -h "$backup_file" | cut -f1))"

echo "Pruning backups older than ${BACKUP_RETENTION_DAYS} day(s) in ${BACKUP_DIR} ..."
find "$BACKUP_DIR" -maxdepth 1 -name 'devopsportal-*.dump' -mtime "+${BACKUP_RETENTION_DAYS}" -print -delete

echo "Backup complete."
