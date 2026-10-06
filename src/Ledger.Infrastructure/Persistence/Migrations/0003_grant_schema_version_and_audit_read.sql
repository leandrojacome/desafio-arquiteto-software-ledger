GRANT SELECT ON schemaversions TO ledger_api, ledger_worker, ledger_readonly;
GRANT SELECT (id, recorded_at, event_type, outcome, details) ON audit_log TO ledger_worker;
