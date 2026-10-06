SELECT b.account_id
FROM account_balances AS b
LEFT JOIN ledger_entries AS e ON e.account_id = b.account_id
WHERE b.account_id = @account_id
GROUP BY b.account_id, b.balance
HAVING b.balance <> COALESCE(SUM(CASE e.type WHEN 'CREDIT' THEN e.amount ELSE -e.amount END), 0);
