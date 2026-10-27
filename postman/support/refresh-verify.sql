-- psql -v refresh_id=... -v user_id=... -f postman/support/refresh-verify.sql
-- Read-only inspection. Use synthetic test data; keep output local.
\set ON_ERROR_STOP on
BEGIN READ ONLY;
SELECT _id, status, attempts, created_at, started_at, completed_at, failure_code
FROM banking.bank_refresh
WHERE _id = :'refresh_id'::uuid AND four_money_user_id = :'user_id'::uuid;

SELECT r.account_id, a.name, a.is_closed, r.status, r.added_transactions,
       r.updated_transactions, r.failure_code, r."from", r.through,
       r.last_successful_sync_at AS task_account_completed_at,
       a.last_successful_sync_at, a.transactions_synced_through
FROM banking.bank_refresh_account r
JOIN banking.bank_refresh j ON j._id = r.refresh_id
JOIN banking.bank_account a ON a._id = r.account_id AND a.fk_user_id = j.four_money_user_id
WHERE j._id = :'refresh_id'::uuid AND j.four_money_user_id = :'user_id'::uuid
ORDER BY r.account_id;

SELECT a._id AS account_id, t._id AS local_transaction_id, t.tink_transaction_id,
       t.amount, t.currency_code, t.status, t.date
FROM banking.bank_account a JOIN banking.bank_transaction t ON t.fk_bank_account_id = a._id
WHERE a.fk_user_id = :'user_id'::uuid
  AND EXISTS (SELECT 1 FROM banking.bank_refresh_account r WHERE r.refresh_id = :'refresh_id'::uuid AND r.account_id = a._id)
ORDER BY a._id, t.tink_transaction_id;

SELECT a._id AS account_id, count(t._id) AS transaction_count,
       count(DISTINCT t.tink_transaction_id) AS unique_provider_transaction_count
FROM banking.bank_account a LEFT JOIN banking.bank_transaction t ON t.fk_bank_account_id = a._id
WHERE a.fk_user_id = :'user_id'::uuid
GROUP BY a._id ORDER BY a._id;
ROLLBACK;
