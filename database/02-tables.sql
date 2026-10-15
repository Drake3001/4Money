CREATE TABLE "user"."user" (
    _id                       UUID         NOT NULL PRIMARY KEY,
    login                     TEXT         NOT NULL,
    name                      TEXT         NOT NULL,
    surname                   TEXT         NOT NULL,
    password_hash             TEXT         NOT NULL,
    email                     TEXT,
    phone                     TEXT,
    birth_date                DATE         NOT NULL,
    created_at                TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    last_login_time           TIMESTAMP    DEFAULT NULL,
    is_active                 BOOLEAN      NOT NULL DEFAULT TRUE,
    icon                      TEXT,
    refresh_token             TEXT         DEFAULT NULL,
    refresh_token_expiry_time TIMESTAMP    DEFAULT NULL,
    tink_user_id              TEXT         DEFAULT NULL,
    CONSTRAINT c_user_birth_date_not_in_future
        CHECK (birth_date <= CURRENT_DATE),
    CONSTRAINT c_user_unique_login
        UNIQUE (login),
    CONSTRAINT c_user_unique_email
        UNIQUE (email),
    CONSTRAINT c_user_unique_tink_user_id
        UNIQUE (tink_user_id)
);

CREATE TABLE "grouping"."group" (
    _id           UUID          NOT NULL PRIMARY KEY,
    name          TEXT          NOT NULL,
    icon          TEXT,
    weekly_limit  NUMERIC(18,2) DEFAULT NULL,
    monthly_limit NUMERIC(18,2) DEFAULT NULL,
    year_limit    NUMERIC(18,2) DEFAULT NULL,
    CONSTRAINT c_group_weekly_limit_above_zero
        CHECK (weekly_limit > 0),
    CONSTRAINT c_group_monthly_limit_above_zero
        CHECK (monthly_limit > 0),
    CONSTRAINT c_group_year_limit_above_zero
        CHECK (year_limit > 0)
);

CREATE TABLE expense.category (
    _id         UUID NOT NULL PRIMARY KEY,
    name        TEXT NOT NULL,
    description TEXT NOT NULL,
    icon        TEXT NOT NULL,
    color       TEXT NOT NULL,
    CONSTRAINT c_category_unique_name
        UNIQUE (name)
);

CREATE TABLE receipt.receipt (
    _id               UUID NOT NULL PRIMARY KEY,
    s3_link           TEXT NOT NULL,
    extraction_result JSONB
);

CREATE TABLE banking.bank_account (
    _id                      UUID    NOT NULL PRIMARY KEY,
    fk_user_id               UUID    NOT NULL,
    name                     TEXT    NOT NULL,
    number                   TEXT    NOT NULL,
    financial_institution_id TEXT    NOT NULL,
    type                     TEXT,
    currency_code            TEXT    NOT NULL,
    iban                     TEXT    NOT NULL,
    credentials_id           TEXT,
    is_closed                BOOLEAN NOT NULL DEFAULT FALSE,
    CONSTRAINT c_bank_account_fk_user_id
        FOREIGN KEY (fk_user_id) REFERENCES "user"."user" (_id),
    CONSTRAINT c_bank_account_unique_fk_user_id_financial_institution_id_number
        UNIQUE (fk_user_id, financial_institution_id, number)
);

CREATE TABLE banking.bank_transaction (
    _id                 UUID          NOT NULL PRIMARY KEY,
    fk_bank_account_id  UUID          NOT NULL,
    amount              NUMERIC(18,2) NOT NULL,
    status              TEXT          NOT NULL,
    description         TEXT,
    date                DATE          NOT NULL,
    currency_code       TEXT          NOT NULL,
    tink_transaction_id TEXT          NOT NULL,
    CONSTRAINT c_bank_transaction_amount_above_zero
        CHECK (amount > 0),
    CONSTRAINT c_bank_transaction_status_valid
        CHECK (status IN ('Booked', 'Pending', 'Cancelled')),
    CONSTRAINT c_bank_transaction_fk_bank_account_id
        FOREIGN KEY (fk_bank_account_id) REFERENCES banking.bank_account (_id),
    CONSTRAINT c_bank_transaction_unique_tink_transaction_id
        UNIQUE (tink_transaction_id)
);

CREATE TABLE expense.expense (
    _id                    UUID          NOT NULL PRIMARY KEY,
    fk_group_id            UUID          NOT NULL,
    fk_bank_transaction_id UUID,
    fk_receipt_id          UUID,
    name                   TEXT          NOT NULL,
    merchant               TEXT,
    amount                 NUMERIC(18,2) NOT NULL,
    transaction_time       TIMESTAMP     NOT NULL,
    CONSTRAINT c_expense_amount_above_zero
        CHECK (amount > 0),
    CONSTRAINT c_expense_fk_group_id
        FOREIGN KEY (fk_group_id) REFERENCES "grouping"."group" (_id),
    CONSTRAINT c_expense_fk_bank_transaction_id
        FOREIGN KEY (fk_bank_transaction_id) REFERENCES banking.bank_transaction (_id),
    CONSTRAINT c_expense_fk_receipt_id
        FOREIGN KEY (fk_receipt_id) REFERENCES receipt.receipt (_id)
);

CREATE TABLE expense.expense_item (
    _id            UUID          NOT NULL PRIMARY KEY,
    fk_expense_id  UUID          NOT NULL,
    fk_category_id UUID          NOT NULL,
    fk_user_id     UUID,
    name           TEXT          NOT NULL,
    unit_price     NUMERIC(18,2) NOT NULL,
    quantity       NUMERIC(18,3) NOT NULL,
    CONSTRAINT c_expense_item_unit_price_above_zero
        CHECK (unit_price > 0),
    CONSTRAINT c_expense_item_quantity_above_zero
        CHECK (quantity > 0),
    CONSTRAINT c_expense_item_fk_expense_id
        FOREIGN KEY (fk_expense_id) REFERENCES expense.expense (_id),
    CONSTRAINT c_expense_item_fk_category_id
        FOREIGN KEY (fk_category_id) REFERENCES expense.category (_id),
    CONSTRAINT c_expense_item_fk_user_id
        FOREIGN KEY (fk_user_id) REFERENCES "user"."user" (_id)
);

CREATE TABLE "grouping".group_member (
    fk_user_id  UUID    NOT NULL,
    fk_group_id UUID    NOT NULL,
    is_admin    BOOLEAN NOT NULL DEFAULT FALSE,
    PRIMARY KEY (fk_user_id, fk_group_id),
    CONSTRAINT c_group_member_fk_group_id
        FOREIGN KEY (fk_group_id) REFERENCES "grouping"."group" (_id),
    CONSTRAINT c_group_member_fk_user_id
        FOREIGN KEY (fk_user_id) REFERENCES "user"."user" (_id)
);

CREATE TABLE "grouping".expense_share (
    fk_expense_id UUID         NOT NULL,
    fk_user_id    UUID         NOT NULL,
    percentage    NUMERIC(5,2) NOT NULL,
    PRIMARY KEY (fk_expense_id, fk_user_id),
    CONSTRAINT c_expense_share_percentage_between_zero_and_hundred
        CHECK (percentage >= 0 AND percentage <= 100),
    CONSTRAINT c_expense_share_fk_user_id
        FOREIGN KEY (fk_user_id) REFERENCES "user"."user" (_id),
    CONSTRAINT c_expense_share_fk_expense_id
        FOREIGN KEY (fk_expense_id) REFERENCES expense.expense (_id)
);

CREATE TABLE notifications.notification (
    _id         UUID      NOT NULL PRIMARY KEY,
    fk_group_id UUID,
    text        TEXT      NOT NULL,
    sent_at     TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT c_notification_fk_group_id
        FOREIGN KEY (fk_group_id) REFERENCES "grouping"."group" (_id)
);

CREATE TABLE limits.category_group_limit (
    fk_category_id UUID          NOT NULL,
    fk_group_id    UUID          NOT NULL,
    weekly_limit   NUMERIC(18,2) DEFAULT NULL,
    monthly_limit  NUMERIC(18,2) DEFAULT NULL,
    year_limit     NUMERIC(18,2) DEFAULT NULL,
    PRIMARY KEY (fk_category_id, fk_group_id),
    CONSTRAINT c_category_group_limit_weekly_limit_above_zero
        CHECK (weekly_limit > 0),
    CONSTRAINT c_category_group_limit_monthly_limit_above_zero
        CHECK (monthly_limit > 0),
    CONSTRAINT c_category_group_limit_year_limit_above_zero
        CHECK (year_limit > 0),
    CONSTRAINT c_category_group_limit_fk_category_id
        FOREIGN KEY (fk_category_id) REFERENCES expense.category (_id),
    CONSTRAINT c_category_group_limit_fk_group_id
        FOREIGN KEY (fk_group_id) REFERENCES "grouping"."group" (_id)
);
