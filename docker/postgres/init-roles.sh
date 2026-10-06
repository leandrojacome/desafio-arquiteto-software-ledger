#!/bin/bash
set -euo pipefail

psql -v ON_ERROR_STOP=1 --quiet \
    --username "${POSTGRES_USER}" \
    --dbname postgres \
    --set migrator_password="${LEDGER_MIGRATOR_PASSWORD:?}" \
    --set api_password="${LEDGER_API_PASSWORD:?}" \
    --set worker_password="${LEDGER_WORKER_PASSWORD:?}" \
    --set readonly_password="${LEDGER_READONLY_PASSWORD:?}" <<'SQL'
CREATE ROLE ledger_migrator LOGIN PASSWORD :'migrator_password';
CREATE ROLE ledger_api LOGIN PASSWORD :'api_password';
CREATE ROLE ledger_worker LOGIN PASSWORD :'worker_password';
CREATE ROLE ledger_readonly LOGIN PASSWORD :'readonly_password';

CREATE DATABASE ledger OWNER ledger_migrator;

REVOKE ALL ON DATABASE ledger FROM PUBLIC;
GRANT CONNECT ON DATABASE ledger TO ledger_api, ledger_worker, ledger_readonly;

\connect ledger

REVOKE ALL ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO ledger_api, ledger_worker, ledger_readonly;
SQL
