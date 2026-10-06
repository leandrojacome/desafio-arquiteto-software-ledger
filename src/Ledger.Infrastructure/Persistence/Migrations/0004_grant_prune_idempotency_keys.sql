GRANT SELECT (account_id, idempotency_key, created_at) ON idempotency_keys TO ledger_worker;
GRANT DELETE ON idempotency_keys TO ledger_worker;

CREATE INDEX ix_idempotency_keys_created_at
    ON idempotency_keys (created_at);
