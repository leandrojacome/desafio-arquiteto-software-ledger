CREATE TABLE account_creation_keys (
    client_id varchar(128) NOT NULL,
    idempotency_key varchar(128) NOT NULL,
    account_id uuid NOT NULL,
    request_hash bytea NOT NULL,
    hash_version smallint NOT NULL DEFAULT 1,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT pk_account_creation_keys PRIMARY KEY (client_id, idempotency_key),
    CONSTRAINT uq_account_creation_keys_account_id UNIQUE (account_id),
    CONSTRAINT fk_account_creation_keys_account_id FOREIGN KEY (account_id) REFERENCES accounts (id) DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT ck_account_creation_keys_request_hash CHECK (octet_length(request_hash) = 32),
    CONSTRAINT ck_account_creation_keys_hash_version CHECK (hash_version >= 1)
);

CREATE INDEX ix_account_creation_keys_created_at
    ON account_creation_keys (created_at);

GRANT SELECT, INSERT ON account_creation_keys TO ledger_api;
GRANT SELECT (client_id, idempotency_key, created_at) ON account_creation_keys TO ledger_worker;
GRANT DELETE ON account_creation_keys TO ledger_worker;
GRANT SELECT (client_id, idempotency_key, account_id, hash_version, created_at) ON account_creation_keys TO ledger_readonly;
