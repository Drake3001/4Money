-- password: TestPassword1!
BEGIN;
SET LOCAL timezone = 'UTC';

WITH demo_users (id, login, name, surname, email, birth_date) AS (
    VALUES
        ('11000000-0000-0000-0000-000000000001', 'demo_anna', 'Anna', 'Testowa', 'demo.anna@example.test', DATE '1998-04-12'),
        ('11000000-0000-0000-0000-000000000002', 'demo_bartek', 'Bartek', 'Testowy', 'demo.bartek@example.test', DATE '1997-08-23')
)
INSERT INTO "user"."user"
    (_id, login, name, surname, password_hash, email, birth_date, created_at, is_active, is_email_confirmed)
SELECT id::uuid, login, name, surname,
    'AQAAAAIAAYagAAAAEEGdALNzDlRQdKeW8O7FH+VJ5F0V5aOCWVsNTRNstKkEdpiKo5ufzXwjA84A/vm2ng==',
    email, birth_date, CURRENT_TIMESTAMP, TRUE, TRUE
FROM demo_users
ON CONFLICT DO NOTHING;

COMMIT;
