CREATE TABLE accounts (
    id uuid NOT NULL,
    currency char(3) NOT NULL,
    holder_document_encrypted bytea NULL,
    holder_document_blind_index bytea NULL,
    holder_document_key_version integer NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT pk_accounts PRIMARY KEY (id),
    CONSTRAINT ck_accounts_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_accounts_holder_document CHECK (
        (holder_document_encrypted IS NULL) = (holder_document_blind_index IS NULL)
        AND (holder_document_encrypted IS NULL) = (holder_document_key_version IS NULL)
    ),
    CONSTRAINT ck_accounts_holder_document_blind_index CHECK (
        holder_document_blind_index IS NULL OR octet_length(holder_document_blind_index) = 32
    )
);

CREATE INDEX ix_accounts_holder_document_blind_index
    ON accounts (holder_document_blind_index)
    WHERE holder_document_blind_index IS NOT NULL;

CREATE INDEX ix_accounts_holder_document_key_version
    ON accounts (holder_document_key_version)
    WHERE holder_document_key_version IS NOT NULL;

CREATE TABLE account_balances (
    account_id uuid NOT NULL,
    balance numeric(18,2) NOT NULL DEFAULT 0,
    overdraft_limit numeric(18,2) NOT NULL DEFAULT 0,
    version bigint NOT NULL DEFAULT 0,
    last_entry_id uuid NULL,
    last_recorded_at timestamptz NOT NULL,
    CONSTRAINT pk_account_balances PRIMARY KEY (account_id),
    CONSTRAINT fk_account_balances_account_id FOREIGN KEY (account_id) REFERENCES accounts (id),
    CONSTRAINT ck_account_balances_overdraft_limit CHECK (overdraft_limit >= 0),
    CONSTRAINT ck_account_balances_version CHECK (version >= 0),
    CONSTRAINT ck_account_balances_balance_floor CHECK (balance >= -overdraft_limit)
) WITH (fillfactor = 70, autovacuum_vacuum_scale_factor = 0.02);

CREATE TABLE ledger_entries (
    id uuid NOT NULL,
    account_id uuid NOT NULL,
    account_version bigint NOT NULL,
    type varchar(6) NOT NULL,
    amount numeric(18,2) NOT NULL,
    currency char(3) NOT NULL,
    balance_after numeric(18,2) NOT NULL,
    recorded_at timestamptz NOT NULL,
    occurred_at timestamptz NOT NULL,
    description varchar(140) NULL,
    reference varchar(100) NULL,
    reverses_entry_id uuid NULL,
    client_id varchar(128) NOT NULL,
    correlation_id varchar(64) NOT NULL,
    CONSTRAINT pk_ledger_entries PRIMARY KEY (id),
    CONSTRAINT fk_ledger_entries_account_id FOREIGN KEY (account_id) REFERENCES accounts (id),
    CONSTRAINT fk_ledger_entries_reverses_entry_id FOREIGN KEY (reverses_entry_id) REFERENCES ledger_entries (id),
    CONSTRAINT uq_ledger_entries_account_id_account_version UNIQUE (account_id, account_version),
    CONSTRAINT ck_ledger_entries_type CHECK (type IN ('CREDIT', 'DEBIT')),
    CONSTRAINT ck_ledger_entries_amount CHECK (amount > 0),
    CONSTRAINT ck_ledger_entries_account_version CHECK (account_version >= 1),
    CONSTRAINT ck_ledger_entries_not_self_reversal CHECK (reverses_entry_id IS NULL OR reverses_entry_id <> id)
) WITH (autovacuum_vacuum_insert_scale_factor = 0.01, autovacuum_vacuum_insert_threshold = 100000);

CREATE INDEX ix_ledger_entries_account_id_recorded_at_account_version
    ON ledger_entries (account_id, recorded_at DESC, account_version DESC)
    INCLUDE (id, balance_after);

CREATE UNIQUE INDEX uq_ledger_entries_reverses_entry_id
    ON ledger_entries (reverses_entry_id)
    WHERE reverses_entry_id IS NOT NULL;

CREATE INDEX ix_ledger_entries_recorded_at
    ON ledger_entries USING brin (recorded_at)
    WITH (pages_per_range = 32, autosummarize = on);

CREATE TABLE idempotency_keys (
    account_id uuid NOT NULL,
    idempotency_key varchar(128) NOT NULL,
    request_hash bytea NOT NULL,
    hash_version smallint NOT NULL DEFAULT 1,
    entry_id uuid NOT NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    CONSTRAINT pk_idempotency_keys PRIMARY KEY (account_id, idempotency_key),
    CONSTRAINT fk_idempotency_keys_account_id FOREIGN KEY (account_id) REFERENCES accounts (id),
    CONSTRAINT fk_idempotency_keys_entry_id FOREIGN KEY (entry_id) REFERENCES ledger_entries (id) DEFERRABLE INITIALLY DEFERRED,
    CONSTRAINT ck_idempotency_keys_request_hash CHECK (octet_length(request_hash) = 32),
    CONSTRAINT ck_idempotency_keys_hash_version CHECK (hash_version >= 1)
);

CREATE TABLE outbox_messages (
    id uuid NOT NULL,
    account_id uuid NOT NULL,
    type varchar(64) NOT NULL,
    payload jsonb NOT NULL,
    correlation_id varchar(64) NOT NULL,
    traceparent varchar(55) NULL,
    created_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    published_at timestamptz NULL,
    locked_until timestamptz NULL,
    attempts integer NOT NULL DEFAULT 0,
    CONSTRAINT pk_outbox_messages PRIMARY KEY (id),
    CONSTRAINT ck_outbox_messages_attempts CHECK (attempts >= 0)
) WITH (fillfactor = 80, autovacuum_vacuum_scale_factor = 0.02);

CREATE INDEX ix_outbox_messages_created_at_pending
    ON outbox_messages (created_at)
    WHERE published_at IS NULL;

CREATE INDEX ix_outbox_messages_published_at
    ON outbox_messages (published_at)
    WHERE published_at IS NOT NULL;

CREATE TABLE audit_log (
    id bigint GENERATED ALWAYS AS IDENTITY,
    recorded_at timestamptz NOT NULL DEFAULT clock_timestamp(),
    event_type varchar(64) NOT NULL,
    client_id varchar(128) NOT NULL,
    account_id uuid NULL,
    correlation_id varchar(64) NOT NULL,
    outcome varchar(16) NOT NULL,
    details jsonb NOT NULL DEFAULT '{}'::jsonb,
    CONSTRAINT pk_audit_log PRIMARY KEY (id),
    CONSTRAINT ck_audit_log_outcome CHECK (outcome IN ('SUCCESS', 'DENIED', 'FAILURE'))
);

CREATE INDEX ix_audit_log_account_id_recorded_at
    ON audit_log (account_id, recorded_at DESC)
    WHERE account_id IS NOT NULL;

CREATE INDEX ix_audit_log_event_type_recorded_at
    ON audit_log (event_type, recorded_at DESC);

CREATE FUNCTION forbid_mutation() RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    RAISE EXCEPTION '% on % is not allowed', TG_OP, TG_TABLE_NAME
        USING ERRCODE = 'integrity_constraint_violation';
END;
$$;

CREATE TRIGGER tr_ledger_entries_forbid_update_delete
    BEFORE UPDATE OR DELETE ON ledger_entries
    FOR EACH ROW EXECUTE FUNCTION forbid_mutation();

CREATE TRIGGER tr_ledger_entries_forbid_truncate
    BEFORE TRUNCATE ON ledger_entries
    FOR EACH STATEMENT EXECUTE FUNCTION forbid_mutation();

CREATE TRIGGER tr_audit_log_forbid_update_delete
    BEFORE UPDATE OR DELETE ON audit_log
    FOR EACH ROW EXECUTE FUNCTION forbid_mutation();

CREATE TRIGGER tr_audit_log_forbid_truncate
    BEFORE TRUNCATE ON audit_log
    FOR EACH STATEMENT EXECUTE FUNCTION forbid_mutation();
