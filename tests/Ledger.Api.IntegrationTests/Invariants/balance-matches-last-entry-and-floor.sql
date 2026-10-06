SELECT b.account_id
FROM account_balances AS b
LEFT JOIN LATERAL (
    SELECT e.id, e.account_version, e.balance_after
    FROM ledger_entries AS e
    WHERE e.account_id = b.account_id
    ORDER BY e.account_version DESC
    LIMIT 1
) AS last_entry ON TRUE
WHERE b.account_id = @account_id
  AND (   b.balance <> COALESCE(last_entry.balance_after, 0)
       OR b.version <> COALESCE(last_entry.account_version, 0)
       OR b.last_entry_id IS DISTINCT FROM last_entry.id
       OR b.balance < -b.overdraft_limit);
