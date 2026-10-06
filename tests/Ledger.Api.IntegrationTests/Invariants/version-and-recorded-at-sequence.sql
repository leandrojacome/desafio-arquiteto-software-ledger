SELECT ordered.id
FROM (
    SELECT e.id,
           e.account_version,
           e.recorded_at,
           LAG(e.account_version) OVER (ORDER BY e.account_version) AS previous_version,
           LAG(e.recorded_at) OVER (ORDER BY e.account_version) AS previous_recorded_at
    FROM ledger_entries AS e
    WHERE e.account_id = @account_id
) AS ordered
WHERE ordered.account_version <> COALESCE(ordered.previous_version, 0) + 1
   OR ordered.recorded_at <= COALESCE(ordered.previous_recorded_at, '-infinity'::timestamptz);
