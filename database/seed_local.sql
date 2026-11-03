-- Destructive local reset: run only after SQL init and all EF migrations, including
-- 20261102215328_AddTemporaryExpenseAggregationSupport (after AddGroupingModel).
-- Shared password for all seeded test accounts: TestPassword1!
-- Storage keys are synthetic; this script does not upload images or call OCR/Tink.
BEGIN;
SET LOCAL timezone = 'UTC';

TRUNCATE TABLE
    grouping.group_settlements,
    grouping.group_invitation,
    limits.group_limit,
    banking.bank_refresh_account,
    banking.bank_refresh,
    banking.bank_connection_attempt,
    banking.bank_connection,
    "user".user_token,
    "user"."user",
    "grouping".group_user,
    "grouping"."group",
    expense.expense_share,
    expense.expense_item,
    expense.expense,
    expense.category,
    banking.bank_transaction,
    banking.bank_account,
    receipt.receipt,
    limits.category_group_limit,
    notifications.notification,
    notifications.notification_recipient;

WITH seed_users (id, login, name, surname, email, birth_date, is_active, is_email_confirmed) AS (
    VALUES
        ('10000000-0000-0000-0000-000000000001', 'seed_anna', 'Anna', 'Testowa', 'anna@example.test', DATE '1998-04-12', TRUE, TRUE),
        ('10000000-0000-0000-0000-000000000002', 'seed_bartek', 'Bartek', 'Testowy', 'bartek@example.test', DATE '1997-08-23', TRUE, TRUE),
        ('10000000-0000-0000-0000-000000000003', 'seed_celina', 'Celina', 'Testowa', 'celina@example.test', DATE '2000-01-05', TRUE, TRUE),
        ('10000000-0000-0000-0000-000000000004', 'seed_dawid', 'Dawid', 'Testowy', 'dawid@example.test', DATE '1999-11-18', FALSE, FALSE),
        ('10000000-0000-0000-0000-000000000010', 'postman.banking', 'Postman', 'Banking', 'postman.banking@example.test', DATE '2000-01-01', TRUE, TRUE)
)
INSERT INTO "user"."user"
    (_id, login, name, surname, password_hash, email, birth_date, created_at, is_active, is_email_confirmed)
SELECT id::uuid, login, name, surname,
    'AQAAAAIAAYagAAAAEEGdALNzDlRQdKeW8O7FH+VJ5F0V5aOCWVsNTRNstKkEdpiKo5ufzXwjA84A/vm2ng==',
    email, birth_date, CURRENT_TIMESTAMP - INTERVAL '30 days', is_active, is_email_confirmed
FROM seed_users;

INSERT INTO "grouping"."group" (id, name, icon, admin_user_id, currency_code, description, icon_storage_key, is_archived) VALUES
    ('20000000-0000-0000-0000-000000000001', 'Dom', 'home', '10000000-0000-0000-0000-000000000001', 'PLN', NULL, NULL, FALSE),
    ('20000000-0000-0000-0000-000000000002', 'Wyjazd', 'suitcase', '10000000-0000-0000-0000-000000000002', 'PLN', NULL, NULL, FALSE);

INSERT INTO grouping.group_user (user_id, group_id, joined_at) VALUES
    ('10000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', CURRENT_TIMESTAMP - INTERVAL '20 days'),
    ('10000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000001', CURRENT_TIMESTAMP - INTERVAL '20 days'),
    ('10000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000002', CURRENT_TIMESTAMP - INTERVAL '20 days'),
    ('10000000-0000-0000-0000-000000000003', '20000000-0000-0000-0000-000000000002', CURRENT_TIMESTAMP - INTERVAL '20 days');

INSERT INTO expense.category (_id, name, description, icon, color) VALUES
    ('30000000-0000-0000-0000-000000000001', 'Zakupy spozywcze', 'Produkty spozywcze do domu', 'shopping-cart', '#4CAF50'),
    ('30000000-0000-0000-0000-000000000002', 'Restauracje', 'Posilki poza domem', 'utensils', '#FF9800'),
    ('30000000-0000-0000-0000-000000000003', 'Transport', 'Bilety i przejazdy', 'bus', '#2196F3'),
    ('30000000-0000-0000-0000-000000000004', 'Mieszkanie', 'Czynsz i rachunki', 'home', '#9C27B0');

INSERT INTO banking.bank_account
    (_id, fk_user_id, name, number, financial_institution_id, type, currency_code, iban, is_closed)
VALUES
    ('40000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'Konto Anny', '61109010140000071219812874', 'seed-bank', 'CHECKING', 'PLN', 'PL61109010140000071219812874', FALSE),
    ('40000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002', 'Konto Bartka', '98102055580000057116184710', 'seed-bank', 'CHECKING', 'PLN', 'PL98102055580000057116184710', FALSE),
    ('40000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', 'Zamkniete konto Anny', '51114020040000300201355387', 'seed-bank', 'SAVINGS', 'PLN', 'PL51114020040000300201355387', TRUE);

INSERT INTO banking.bank_transaction
    (_id, fk_bank_account_id, amount, status, description, date, currency_code, tink_transaction_id)
VALUES
    ('50000000-0000-0000-0000-000000000001', '40000000-0000-0000-0000-000000000001', -120.00, 'Booked', 'Zakupy spozywcze', CURRENT_DATE - 2, 'PLN', 'seed-transaction-001'),
    ('50000000-0000-0000-0000-000000000002', '40000000-0000-0000-0000-000000000002', -60.00, 'Booked', 'Bilety na wyjazd', CURRENT_DATE - 1, 'PLN', 'seed-transaction-002'),
    ('50000000-0000-0000-0000-000000000003', '40000000-0000-0000-0000-000000000001', -45.00, 'Pending', 'Platnosc oczekujaca', CURRENT_DATE, 'PLN', 'seed-transaction-003'),
    ('50000000-0000-0000-0000-000000000004', '40000000-0000-0000-0000-000000000002', -30.00, 'Cancelled', 'Platnosc anulowana', CURRENT_DATE - 3, 'PLN', 'seed-transaction-004'),
    ('50000000-0000-0000-0000-000000000005', '40000000-0000-0000-0000-000000000002', -30.00, 'Booked', 'Syntetyczne rozliczenie', CURRENT_DATE, 'PLN', 'seed-settlement-005');

INSERT INTO receipt.receipt (_id, storage_key, extraction_result) VALUES
    ('60000000-0000-0000-0000-000000000001', 'receipts/60000000000000000000000000000001',
     '{
       "receipt": {
         "store": "Sklep testowy",
         "date": null,
         "currency": "PLN",
         "total": "120.00",
         "amount_due": "120.00",
         "items": [
           {"name":"Chleb","quantity":"2.000","unit":"szt","unit_price":"10.00","total":"20.00","before_discount":null,"discount":null,"name_corrected":null,"category":"Zakupy spozywcze","needs_review":false,"review_reason":null},
           {"name":"Mleko","quantity":"4.000","unit":"szt","unit_price":"5.00","total":"20.00","before_discount":null,"discount":null,"name_corrected":null,"category":"Zakupy spozywcze","needs_review":false,"review_reason":null},
           {"name":"Warzywa","quantity":"4.000","unit":"kg","unit_price":"20.00","total":"80.00","before_discount":null,"discount":null,"name_corrected":null,"category":"Zakupy spozywcze","needs_review":false,"review_reason":null}
         ],
         "deposits": [],
         "deposit_returns": []
       },
       "warnings": [],
       "needs_review": false
     }'::jsonb);

INSERT INTO expense.expense
    (_id, fk_group_id, fk_bank_transaction_id, fk_receipt_id, name, merchant, amount, transaction_time, fk_payer_id)
VALUES
    ('70000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', '50000000-0000-0000-0000-000000000001', '60000000-0000-0000-0000-000000000001', 'Zakupy do domu', 'Sklep testowy', 120.00, CURRENT_TIMESTAMP - INTERVAL '2 days', '10000000-0000-0000-0000-000000000001'),
    ('70000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000002', NULL, NULL, 'Wspolny obiad', 'Restauracja testowa', 90.00, CURRENT_TIMESTAMP - INTERVAL '1 day', '10000000-0000-0000-0000-000000000002'),
    ('70000000-0000-0000-0000-000000000003', '20000000-0000-0000-0000-000000000002', '50000000-0000-0000-0000-000000000002', NULL, 'Bilety na wyjazd', 'Przewoznik testowy', 60.00, CURRENT_TIMESTAMP - INTERVAL '1 day', '10000000-0000-0000-0000-000000000002'),
    ('70000000-0000-0000-0000-000000000004', '20000000-0000-0000-0000-000000000001', NULL, NULL, 'Czynsz', NULL, 1500.00, CURRENT_TIMESTAMP - INTERVAL '5 days', '10000000-0000-0000-0000-000000000001');

INSERT INTO expense.expense_item
    (_id, fk_expense_id, fk_category_id, fk_user_id, name, unit_price, quantity)
VALUES
    ('80000000-0000-0000-0000-000000000001', '70000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', NULL, 'Chleb', 10.00, 2.000),
    ('80000000-0000-0000-0000-000000000002', '70000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', NULL, 'Mleko', 5.00, 4.000),
    ('80000000-0000-0000-0000-000000000003', '70000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000001', NULL, 'Warzywa', 20.00, 4.000),
    ('80000000-0000-0000-0000-000000000004', '70000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002', 'Obiad Bartka', 50.00, 1.000),
    ('80000000-0000-0000-0000-000000000005', '70000000-0000-0000-0000-000000000002', '30000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000003', 'Obiad Celiny', 40.00, 1.000),
    ('80000000-0000-0000-0000-000000000006', '70000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003', NULL, 'Bilet', 30.00, 2.000),
    ('80000000-0000-0000-0000-000000000007', '70000000-0000-0000-0000-000000000004', '30000000-0000-0000-0000-000000000004', NULL, 'Czynsz za mieszkanie', 1500.00, 1.000);

INSERT INTO expense.expense_share (fk_expense_id, fk_user_id, amount) VALUES
    ('70000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 60.00),
    ('70000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', 60.00),
    ('70000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002', 50.00),
    ('70000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000003', 40.00),
    ('70000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000002', 30.00),
    ('70000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000003', 30.00),
    ('70000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000001', 900.00),
    ('70000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000002', 600.00);

INSERT INTO limits.category_group_limit
    (fk_category_id, fk_group_id, weekly_limit, monthly_limit, year_limit)
VALUES
    ('30000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', 300.00, 1200.00, 14400.00),
    ('30000000-0000-0000-0000-000000000004', '20000000-0000-0000-0000-000000000001', NULL, 2500.00, 30000.00),
    ('30000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000002', NULL, 400.00, NULL),
    ('30000000-0000-0000-0000-000000000003', '20000000-0000-0000-0000-000000000002', NULL, 300.00, NULL);

INSERT INTO notifications.notification
    (_id, fk_group_id, type, title, text, created_at, fk_expense_id)
VALUES
    ('90000000-0000-0000-0000-000000000001', NULL, 1, 'Nowy wydatek', 'Dodano wydatek: Zakupy do domu.', CURRENT_TIMESTAMP - INTERVAL '2 days', '70000000-0000-0000-0000-000000000001'),
    ('90000000-0000-0000-0000-000000000002', NULL, 1, 'Nowy wydatek', 'Dodano wydatek: Bilety na wyjazd.', CURRENT_TIMESTAMP - INTERVAL '1 day', '70000000-0000-0000-0000-000000000003'),
    ('90000000-0000-0000-0000-000000000003', NULL, 0, 'Informacja', 'Przykladowe powiadomienie ogolne.', CURRENT_TIMESTAMP, NULL),
    ('90000000-0000-0000-0000-000000000004', '20000000-0000-0000-0000-000000000001', 4, 'Nowy czlonek grupy', 'Bartek dolaczyl do grupy Dom.', CURRENT_TIMESTAMP - INTERVAL '3 days', NULL);

INSERT INTO notifications.notification_recipient (fk_notification_id, fk_user_id, read_at) VALUES
    ('90000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', CURRENT_TIMESTAMP - INTERVAL '1 day'),
    ('90000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', NULL),
    ('90000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000002', NULL),
    ('90000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000003', CURRENT_TIMESTAMP - INTERVAL '12 hours'),
    ('90000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000001', NULL),
    ('90000000-0000-0000-0000-000000000004', '10000000-0000-0000-0000-000000000001', CURRENT_TIMESTAMP - INTERVAL '2 days');

INSERT INTO grouping.group_settlements
    (id, group_id, from_user_id, to_user_id, amount, method, settled_at, bank_transaction_id)
VALUES
    ('a0000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 100.00, 1, CURRENT_TIMESTAMP, NULL),
    ('a0000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000001', 30.00, 2, CURRENT_TIMESTAMP, '50000000-0000-0000-0000-000000000005'),
    ('a0000000-0000-0000-0000-000000000003', '20000000-0000-0000-0000-000000000002', '10000000-0000-0000-0000-000000000003', '10000000-0000-0000-0000-000000000002', 10.00, 3, CURRENT_TIMESTAMP, NULL);

INSERT INTO grouping.group_invitation (id, group_id, created_by_user_id, token_hash, max_uses, use_count, expires_at)
VALUES ('b0000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', '10000000-0000-0000-0000-000000000001', 'synthetic-hash-not-a-real-token', 5, 1, CURRENT_TIMESTAMP + INTERVAL '7 days');

INSERT INTO limits.group_limit (id, group_id, period, amount) VALUES
    ('c0000000-0000-0000-0000-000000000001', '20000000-0000-0000-0000-000000000001', 1, 1000.00),
    ('c0000000-0000-0000-0000-000000000002', '20000000-0000-0000-0000-000000000001', 2, 4000.00),
    ('c0000000-0000-0000-0000-000000000003', '20000000-0000-0000-0000-000000000001', 3, 48000.00);

-- Rebuild the synthetic aggregates from their sources, never independent constants.
UPDATE grouping.group_user m SET
    total_paid = COALESCE((SELECT SUM(e.amount) FROM expense.expense e WHERE e.fk_group_id = m.group_id AND e.fk_payer_id = m.user_id), 0),
    total_share = COALESCE((SELECT SUM(s.amount) FROM expense.expense_share s JOIN expense.expense e ON e._id = s.fk_expense_id WHERE e.fk_group_id = m.group_id AND s.fk_user_id = m.user_id), 0),
    total_settlements_sent = COALESCE((SELECT SUM(s.amount) FROM grouping.group_settlements s WHERE s.group_id = m.group_id AND s.from_user_id = m.user_id), 0),
    total_settlements_received = COALESCE((SELECT SUM(s.amount) FROM grouping.group_settlements s WHERE s.group_id = m.group_id AND s.to_user_id = m.user_id), 0);

COMMIT;

SELECT 'user.user' AS "table", COUNT(*) AS row_count FROM "user"."user"
UNION ALL SELECT 'user.user_token', COUNT(*) FROM "user".user_token
UNION ALL SELECT 'grouping.group', COUNT(*) FROM "grouping"."group"
UNION ALL SELECT 'grouping.group_user', COUNT(*) FROM "grouping".group_user
UNION ALL SELECT 'grouping.group_settlements', COUNT(*) FROM grouping.group_settlements
UNION ALL SELECT 'grouping.group_invitation', COUNT(*) FROM grouping.group_invitation
UNION ALL SELECT 'limits.group_limit', COUNT(*) FROM limits.group_limit
UNION ALL SELECT 'expense.category', COUNT(*) FROM expense.category
UNION ALL SELECT 'expense.expense', COUNT(*) FROM expense.expense
UNION ALL SELECT 'expense.expense_item', COUNT(*) FROM expense.expense_item
UNION ALL SELECT 'expense.expense_share', COUNT(*) FROM expense.expense_share
UNION ALL SELECT 'banking.bank_refresh', COUNT(*) FROM banking.bank_refresh
UNION ALL SELECT 'banking.bank_refresh_account', COUNT(*) FROM banking.bank_refresh_account
UNION ALL SELECT 'banking.bank_connection', COUNT(*) FROM banking.bank_connection
UNION ALL SELECT 'banking.bank_connection_attempt', COUNT(*) FROM banking.bank_connection_attempt
UNION ALL SELECT 'banking.bank_account', COUNT(*) FROM banking.bank_account
UNION ALL SELECT 'banking.bank_transaction', COUNT(*) FROM banking.bank_transaction
UNION ALL SELECT 'receipt.receipt', COUNT(*) FROM receipt.receipt
UNION ALL SELECT 'limits.category_group_limit', COUNT(*) FROM limits.category_group_limit
UNION ALL SELECT 'notifications.notification', COUNT(*) FROM notifications.notification
UNION ALL SELECT 'notifications.notification_recipient', COUNT(*) FROM notifications.notification_recipient
ORDER BY "table";
