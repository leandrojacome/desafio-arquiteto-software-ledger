#!/bin/sh
set -eu

status=$(sed -n '8p' "${PGDATA:-/var/lib/postgresql/data}/postmaster.pid" 2>/dev/null | tr -d ' ' || true)
[ "${status}" = "ready" ] || exit 1

PGPASSWORD="${POSTGRES_PASSWORD}" psql -h 127.0.0.1 -U postgres -d ledger -tAc 'select 1' | grep -q 1
