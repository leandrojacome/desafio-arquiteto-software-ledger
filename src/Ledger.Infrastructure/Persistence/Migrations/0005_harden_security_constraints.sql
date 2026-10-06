REVOKE UPDATE ON account_balances FROM ledger_api;
GRANT UPDATE (balance, version, last_entry_id, last_recorded_at) ON account_balances TO ledger_api;

REVOKE UPDATE ON accounts FROM ledger_worker;
GRANT UPDATE (holder_document_encrypted, holder_document_blind_index, holder_document_key_version)
    ON accounts TO ledger_worker;

REVOKE UPDATE ON outbox_messages FROM ledger_worker;
GRANT UPDATE (published_at, locked_until, attempts) ON outbox_messages TO ledger_worker;

REVOKE SELECT ON accounts FROM ledger_api;
GRANT SELECT (id, currency, created_at) ON accounts TO ledger_api;

ALTER TABLE accounts
    ADD CONSTRAINT ck_accounts_holder_document_key_version_matches_payload CHECK (
        holder_document_encrypted IS NULL
        OR (CASE
                WHEN octet_length(holder_document_encrypted) >= 3
                    THEN get_byte(holder_document_encrypted, 1) * 256 + get_byte(holder_document_encrypted, 2)
                         = holder_document_key_version
                ELSE FALSE
            END)
    );
