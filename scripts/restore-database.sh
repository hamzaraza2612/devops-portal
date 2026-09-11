#!/usr/bin/env bash
# Restores a pg_dump custom-format archive (as produced by
# backup-database.sh) into a Postgres database. Deliberately requires an
# explicit --yes confirmation flag before touching anything — a restore is a
# destructive operation against whatever database PGDATABASE currently
# names, and this script never guesses that a human meant to overwrite
# production.
#
# Usage:
#   PGHOST=localhost PGPORT=5432 PGUSER=devopsportal PGDATABASE=devopsportal \
#   PGPASSWORD=*** ./scripts/restore-database.sh path/to/backup.dump --yes
#
# Recommended restore-verification procedure (do this after every backup
# rotation, not just when an incident forces an actual restore): run this
# script with PGDATABASE pointed at a throwaway/staging database, confirm
# the application starts against it and `dotnet ef migrations
# has-pending-model-changes` reports none, then drop the throwaway database.
# This is the only way to actually know a backup is restorable, rather than
# just present on disk.

set -euo pipefail

backup_file="${1:-}"
confirm_flag="${2:-}"

if [[ -z "$backup_file" ]]; then
  echo "Usage: $0 <backup-file.dump> --yes" >&2
  exit 1
fi
if [[ ! -f "$backup_file" ]]; then
  echo "ERROR: backup file not found: $backup_file" >&2
  exit 1
fi
if [[ "$confirm_flag" != "--yes" ]]; then
  echo "This will DROP and recreate objects in database '${PGDATABASE:-<unset>}' on host '${PGHOST:-<unset>}'." >&2
  echo "Re-run with --yes to confirm: $0 $backup_file --yes" >&2
  exit 1
fi

: "${PGDATABASE:?PGDATABASE is required}"
: "${PGUSER:?PGUSER is required}"

echo "Restoring ${backup_file} into database '${PGDATABASE}' ..."
pg_restore --clean --if-exists --no-owner --no-privileges --dbname="$PGDATABASE" "$backup_file"

echo "Restore complete. Verify with: dotnet ef migrations has-pending-model-changes (from src/DevOpsPortal.Api) and an application smoke test before treating this database as live."
