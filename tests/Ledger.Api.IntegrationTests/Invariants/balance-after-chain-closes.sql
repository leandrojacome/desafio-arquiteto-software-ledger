SELECT chain.id
FROM (
    SELECT e.id,
           e.balance_after,
           COALESCE(LAG(e.balance_after) OVER (ORDER BY e.account_version), 0)
             + CASE e.type WHEN 'CREDIT' THEN e.amount ELSE -e.amount END AS expected_balance_after
    FROM ledger_entries AS e
    WHERE e.account_id = @account_id
) AS chain
WHERE chain.balance_after <> chain.expected_balance_after;
